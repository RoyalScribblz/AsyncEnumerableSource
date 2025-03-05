using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AsyncEnumerableSource;

public abstract class AsyncEnumerableSource
{
    protected static readonly UnboundedChannelOptions ChannelOptions = new()
    {
        SingleWriter = true,
        SingleReader = true,
    };
}

public sealed class AsyncEnumerableSource<T> : AsyncEnumerableSource
{
    private readonly ConcurrentDictionary<Channel<T>, byte> _channels = [];
    private bool _completed;
    private Exception? _exception;

    public async IAsyncEnumerable<T> GetAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_exception != null)
        {
            throw _exception;
        }

        if (_completed)
        {
            yield break;
        }

        var channel = Channel.CreateUnbounded<T>(ChannelOptions);
        _channels[channel] = 0; 
        
        try
        {
            // https://stackoverflow.com/questions/67569758/channelreader-readallasynccancellationtoken-not-actually-cancelled-mid-iterati
            await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
                
                if (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }
            }
        }
        finally
        {
            _channels.TryRemove(channel, out _);
        }
    }

    public void YieldReturn(T value)
    {
        if (_completed)
        {
            return;
        }

        Parallel.ForEach(_channels.Keys, channel => channel.Writer.TryWrite(value));
    }

    public void Complete()
    {
        if (Interlocked.CompareExchange(ref _completed, true, false))
        {
            return;
        }

        Parallel.ForEach(_channels.Keys, channel => channel.Writer.TryComplete());
    }

    public void Fault(Exception error)
    {
        if (Interlocked.CompareExchange(ref _exception, error, null) != null)
        {
            return;
        }
        
        if (Interlocked.CompareExchange(ref _completed, true, false))
        {
            return;
        }

        Parallel.ForEach(_channels.Keys, channel => channel.Writer.TryComplete(error));
    }
}