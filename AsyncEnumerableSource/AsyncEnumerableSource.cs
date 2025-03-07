using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#if NET6_0_OR_GREATER
using System.Runtime.InteropServices;
#endif
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AsyncEnumerableSource
{
    public abstract class AsyncEnumerableSource
    {
        protected static readonly UnboundedChannelOptions UnboundedChannelOptions = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true,
        };
        
        protected static BoundedChannelOptions BoundedChannelOptions(int capacity) => new BoundedChannelOptions(capacity)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        };
    }

    public sealed class AsyncEnumerableSource<T> : AsyncEnumerableSource
    {
        private readonly List<Channel<T>> _channels = new List<Channel<T>>();
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private int _completed;
        private Exception _exception;
        private readonly int? _boundedCapacity;
        
        private const int True = 1;
        
        public AsyncEnumerableSource(int? boundedCapacity = null)
        {
            _boundedCapacity = boundedCapacity;
        }
        
        public async IAsyncEnumerable<T> GetAsyncEnumerable(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_exception != null)
            {
                throw _exception;
            }

            if (_completed == True)
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
                // https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channelreader-1.readallasync?view=net-9.0#parameters
                await foreach (var item in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
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

        public async Task YieldReturn(T value)
        {
            if (_completed == True)
            {
                return;
            }

            Channel<T>[] channelsSnapshot;
            int consumerCount;

            _lock.EnterReadLock();
            try
            {
                consumerCount = _channels.Count;
                
                if (consumerCount == 0)
                {
                    return;
                }

                channelsSnapshot = ArrayPool<Channel<T>>.Shared.Rent(consumerCount);
#if NET6_0_OR_GREATER
                if (consumerCount <= 5000)
                {
                    CollectionsMarshal.AsSpan(_channels).CopyTo(channelsSnapshot);
                }
                else
                {
                    _channels.CopyTo(channelsSnapshot);
                }
#else
                _channels.CopyTo(channelsSnapshot);
#endif
            }
            finally
            {
                _lock.ExitReadLock();
            }

            try
            {
#if NET8_0_OR_GREATER
                if (consumerCount >= 50)
                {
                    await Parallel.ForAsync(0, consumerCount,
                        async (index, ct) => await channelsSnapshot[index].Writer.WriteAsync(value, ct));
                }
                else
                {
                    for (var index = 0; index < consumerCount; index++)
                    {
                        await channelsSnapshot[index].Writer.WriteAsync(value);
                    }
                }
#else
                for (var index = 0; index < consumerCount; index++)
                {
                    await channelsSnapshot[index].Writer.WriteAsync(value);
                }
#endif
            }
            finally
            {
                if (channelsSnapshot != null)
                {
                    ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
                }
            }
        }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, True) == True)
            {
                return;
            }

            Channel<T>[] channelsSnapshot;
            int consumerCount;

            _lock.EnterReadLock();
            try
            {
                consumerCount = _channels.Count;
                if (consumerCount == 0)
                {
                    return;
                }

                channelsSnapshot = ArrayPool<Channel<T>>.Shared.Rent(consumerCount);
#if NET6_0_OR_GREATER
                if (consumerCount <= 5000)
                {
                    CollectionsMarshal.AsSpan(_channels).CopyTo(channelsSnapshot);
                }
                else
                {
                    _channels.CopyTo(channelsSnapshot);
                }
#else
                _channels.CopyTo(channelsSnapshot);
#endif
            }
            finally
            {
                _lock.ExitReadLock();
            }

            try
            {
                if (consumerCount >= 50)
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
                if (channelsSnapshot != null)
                {
                    ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
                }
            }
        }

        public void Fault(Exception error)
        {
            if (Interlocked.CompareExchange(ref _exception, error, null) != null)
            {
                return;
            }

            if (Interlocked.Exchange(ref _completed, True) == True)
            {
                return;
            }

            Channel<T>[] channelsSnapshot;
            int consumerCount;

            _lock.EnterReadLock();
            try
            {
                consumerCount = _channels.Count;
                if (consumerCount == 0)
                {
                    return;
                }

                channelsSnapshot = ArrayPool<Channel<T>>.Shared.Rent(consumerCount);
#if NET6_0_OR_GREATER
                if (consumerCount <= 5000)
                {
                    CollectionsMarshal.AsSpan(_channels).CopyTo(channelsSnapshot);
                }
                else
                {
                    _channels.CopyTo(channelsSnapshot);
                }
#else
                _channels.CopyTo(channelsSnapshot);
#endif
            }
            finally
            {
                _lock.ExitReadLock();
            }

            try
            {
                if (consumerCount >= 50)
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
                if (channelsSnapshot != null)
                {
                    ArrayPool<Channel<T>>.Shared.Return(channelsSnapshot);
                }
            }
        }
    }
}