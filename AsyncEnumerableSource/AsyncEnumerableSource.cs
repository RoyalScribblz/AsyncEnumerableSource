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
        protected static readonly UnboundedChannelOptions ChannelOptions = new UnboundedChannelOptions()
        {
            SingleWriter = true,
            SingleReader = true,
        };
    }

    public sealed class AsyncEnumerableSource<T> : AsyncEnumerableSource
    {
        private readonly List<Channel<T>> _channels = new List<Channel<T>>();
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private int _completed;
        private Exception _exception;
        
        private const int True = 1;
        
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

            var channel = Channel.CreateUnbounded<T>(ChannelOptions);
        
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

        public void YieldReturn(T value)
        {
            if (_completed == True)
            {
                return;
            }

            Channel<T>[] channelsSnapshot;
            int count;

            _lock.EnterReadLock();
            try
            {
                count = _channels.Count;
                
                if (count == 0)
                {
                    return;
                }

                channelsSnapshot = ArrayPool<Channel<T>>.Shared.Rent(count);
#if NET6_0_OR_GREATER
                if (count <= 5000)
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
                if (count >= 50)
                {
                    Parallel.For(0, count, index => channelsSnapshot[index].Writer.TryWrite(value));
                }
                else
                {
                    foreach (var channel in channelsSnapshot.AsSpan(0, count))
                    {
                        channel.Writer.TryWrite(value);
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

        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, True) == True)
            {
                return;
            }

            Channel<T>[] channelsSnapshot;
            int count;

            _lock.EnterReadLock();
            try
            {
                count = _channels.Count;
                if (count == 0)
                {
                    return;
                }

                channelsSnapshot = ArrayPool<Channel<T>>.Shared.Rent(count);
#if NET6_0_OR_GREATER
                if (count <= 5000)
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
                if (count >= 50)
                {
                    Parallel.For(0, count, index => channelsSnapshot[index].Writer.TryComplete());
                }
                else
                {
                    foreach (var channel in channelsSnapshot.AsSpan(0, count))
                    {
                        channel.Writer.TryComplete();
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
            int count;

            _lock.EnterReadLock();
            try
            {
                count = _channels.Count;
                if (count == 0)
                {
                    return;
                }

                channelsSnapshot = ArrayPool<Channel<T>>.Shared.Rent(count);
#if NET6_0_OR_GREATER
                if (count <= 5000)
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
                if (count >= 50)
                {
                    Parallel.For(0, count, index => channelsSnapshot[index].Writer.TryComplete(error));
                }
                else
                {
                    for (var index = 0; index < count; index++)
                    {
                        channelsSnapshot[index].Writer.TryComplete(error);
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