using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace JLloyd.AsyncSources;

/// <summary>
/// Base class for providing channel option statics for bounded and unbounded channels.
/// </summary>
public abstract class AsyncEnumerableSource
{
    /// <summary>
    /// Defines the options for an unbounded channel.
    /// </summary>
    protected static readonly UnboundedChannelOptions UnboundedChannelOptions = new()
    {
        SingleWriter = true,
        SingleReader = true,
    };

    /// <summary>
    /// Creates options for a bounded channel with the specified capacity.
    /// </summary>
    /// <param name="capacity">The maximum number of items in the channel.</param>
    /// <returns>A configured <see cref="System.Threading.Channels.BoundedChannelOptions"/> instance.</returns>
    protected static BoundedChannelOptions BoundedChannelOptions(int capacity) => new(capacity)
    {
        SingleWriter = true,
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait,
    };
}

/// <summary>
/// Provides an <see cref="AsyncEnumerableSource{T}"/> that supports multiple consumers of the same data.
/// </summary>
public sealed class AsyncEnumerableSource<T> : AsyncEnumerableSource, IDisposable
{
    /// <summary>
    /// List of channels for distributing data to consumers.
    /// </summary>
    private readonly List<Channel<T>> _channels = [];

    /// <summary>
    /// Lock for synchronising access to <see cref="_channels"/>.
    /// </summary>
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>
    /// Indicates whether the source has been completed.
    /// </summary>
    private bool _completed;

    /// <summary>
    /// Stores the exception set by <see cref="Fault"/>.
    /// </summary>
    private Exception? _exception;

    /// <summary>
    /// Optional bounded capacity for the channel.
    /// </summary>
    private readonly int? _boundedCapacity;

    /// <summary>
    /// Threshold at which <c>CollectionsMarshal.AsSpan().CopyTo()</c> becomes slower than <c>.CopyTo()</c>.
    /// </summary>
    private const int AsSpanCopyToThreshold = 5000;

    /// <summary>
    /// Threshold at which <c>await Task.WhenAll()</c> becomes faster than <c>Parallel.ForEach()</c> and <c>foreach</c> when the channels are bounded.
    /// </summary>
    private const int WhenAllBoundedThreshold = 10;

    /// <summary>
    /// Threshold at which <see cref="Parallel"/> methods become faster than <c>for</c> and <c>foreach</c> for <see cref="Channel{T}"/> writes.
    /// </summary>
    private const int ParallelWriteThreshold = 50;

    /// <summary>
    /// Initializes a new instance of <see cref="AsyncEnumerableSource{T}"/>.
    /// </summary>
    /// <param name="boundedCapacity">Optional bounded capacity for the channels.</param>
    public AsyncEnumerableSource(int? boundedCapacity = null)
    {
        _boundedCapacity = boundedCapacity;
    }

    /// <summary>
    /// Gets an asynchronous enumerable to consume the source asynchronously.
    /// </summary>
    /// <param name="ct">Token to cancel the enumeration.</param>
    /// <returns>An asynchronous sequence of values yielded by the source.</returns>
    public async IAsyncEnumerable<T> GetAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_exception != null)
        {
            throw _exception;
        }

        if (_completed)
        {
            yield break;
        }

        var channel = _boundedCapacity.HasValue
            ? Channel.CreateBounded<T>(BoundedChannelOptions(_boundedCapacity.Value))
            : Channel.CreateUnbounded<T>(UnboundedChannelOptions);

        _lock.EnterWriteLock();
        try
        {
            _channels.Add(channel);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        try
        {
            // https://learn.microsoft.com/dotnet/api/system.threading.channels.channelreader-1.readallasync#parameters
            await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested)
                {
                    yield break;
                }

                yield return item;
            }
        }
        finally
        {
            _lock.EnterWriteLock();
            try
            {
                _channels.Remove(channel);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    /// <summary>
    /// Yields a value to all consumers of the <see cref="AsyncEnumerableSource{T}"/>.
    /// </summary>
    /// <param name="value">The value to yield.</param>
    public async ValueTask YieldReturn(T value)
    {
        if (_completed || !TryRentChannelsSnapshot(out var channelsSnapshot, out var consumerCount))
        {
            return;
        }

        try
        {
            await WriteToChannels(value, channelsSnapshot, consumerCount).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
        }
    }

    /// <summary>
    /// Yields values to all consumers of the <see cref="AsyncEnumerableSource{T}"/>.
    /// </summary>
    /// <param name="values">The values to yield.</param>
    /// <param name="ct">Token to cancel the write operation.</param>
    public async ValueTask YieldReturn(IEnumerable<T> values, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (_completed || !TryRentChannelsSnapshot(out var channelsSnapshot, out var consumerCount))
        {
            return;
        }

        try
        {
            foreach (var value in values)
            {
                ct.ThrowIfCancellationRequested();
                await WriteToChannels(value, channelsSnapshot, consumerCount, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
        }
    }

    /// <summary>
    /// Yields values to all consumers of the <see cref="AsyncEnumerableSource{T}"/>.
    /// </summary>
    /// <param name="values">The values to yield.</param>
    /// <param name="ct">Token to cancel the write operation.</param>
    public async ValueTask YieldReturn(IAsyncEnumerable<T> values, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (_completed || !TryRentChannelsSnapshot(out var channelsSnapshot, out var consumerCount))
        {
            return;
        }

        try
        {
            await foreach (var value in values.WithCancellation(ct).ConfigureAwait(false))
            {
                await WriteToChannels(value, channelsSnapshot, consumerCount, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
        }
    }

    /// <summary>
    /// Marks the source as complete and completes all channels.
    /// </summary>
    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, true) ||
            !TryRentChannelsSnapshot(out var channelsSnapshot, out var consumerCount))
        {
            return;
        }

        try
        {
            if (consumerCount >= ParallelWriteThreshold)
            {
                Parallel.For(0, consumerCount, index => channelsSnapshot[index].Writer.Complete());
            }
            else
            {
                for (var index = 0; index < consumerCount; index++)
                {
                    channelsSnapshot[index].Writer.Complete();
                }
            }
        }
        finally
        {
            ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
        }
    }

    /// <summary>
    /// Marks the source as faulted and propagates an error to all consumers.
    /// </summary>
    /// <param name="error">The exception to propagate.</param>
    public void Fault(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (Interlocked.CompareExchange(ref _exception, error, null) != null ||
            Interlocked.Exchange(ref _completed, true) ||
            !TryRentChannelsSnapshot(out var channelsSnapshot, out var consumerCount))
        {
            return;
        }

        try
        {
            if (consumerCount >= ParallelWriteThreshold)
            {
                Parallel.For(0, consumerCount, index => channelsSnapshot[index].Writer.Complete(error));
            }
            else
            {
                for (var index = 0; index < consumerCount; index++)
                {
                    channelsSnapshot[index].Writer.Complete(error);
                }
            }
        }
        finally
        {
            ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
        }
    }

    /// <summary>
    /// Releases all resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        _lock.Dispose();
    }

    private bool TryRentChannelsSnapshot(out Channel<T>[] channelsSnapshot, out int consumerCount)
    {
        _lock.EnterReadLock();
        try
        {
            consumerCount = _channels.Count;
            if (consumerCount == 0)
            {
                channelsSnapshot = [];
                return false;
            }

            channelsSnapshot = ArrayPool<Channel<T>>.Shared.Rent(consumerCount);
            if (consumerCount <= AsSpanCopyToThreshold)
            {
                CollectionsMarshal.AsSpan(_channels).CopyTo(channelsSnapshot);
            }
            else
            {
                _channels.CopyTo(channelsSnapshot);
            }

            return true;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private async ValueTask WriteToChannels(
        T value,
        Channel<T>[] channelsSnapshot,
        int consumerCount,
        CancellationToken ct = default)
    {
        if (consumerCount >= WhenAllBoundedThreshold && _boundedCapacity.HasValue)
        {
            var tasks = new Task[consumerCount];
            for (var index = 0; index < consumerCount; index++)
            {
                tasks[index] = channelsSnapshot[index].Writer.WriteAsync(value, ct).AsTask();
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else if (consumerCount >= ParallelWriteThreshold)
        {
            await Parallel.ForAsync(
                0,
                consumerCount,
                ct,
                (index, writeCt) => channelsSnapshot[index].Writer.WriteAsync(value, writeCt)).ConfigureAwait(false);
        }
        else
        {
            for (var index = 0; index < consumerCount; index++)
            {
                await channelsSnapshot[index].Writer.WriteAsync(value, ct).ConfigureAwait(false);
            }
        }
    }
}
