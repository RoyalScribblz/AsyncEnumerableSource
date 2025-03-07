using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

            List<Channel<T>> channelsSnapshot;
            _lock.EnterReadLock();
            try
            {
                channelsSnapshot = new List<Channel<T>>(_channels);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        
            if (channelsSnapshot.Count >= 50)
            {
                Parallel.ForEach(channelsSnapshot, channel => channel.Writer.TryWrite(value));
            }
            else
            {
                foreach (var channelsKey in channelsSnapshot)
                {
                    channelsKey.Writer.TryWrite(value);
                }
            }
        }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, True) == True)
            {
                return;
            }

            List<Channel<T>> channelsSnapshot;
            _lock.EnterReadLock();
            try
            {
                channelsSnapshot = new List<Channel<T>>(_channels);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        
            if (_channels.Count >= 50)
            {
                Parallel.ForEach(channelsSnapshot, channel => channel.Writer.TryComplete());
            }
            else
            {
                foreach (var channelsKey in channelsSnapshot)
                {
                    channelsKey.Writer.TryComplete();
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
        
            List<Channel<T>> channelsSnapshot;
            _lock.EnterReadLock();
            try
            {
                channelsSnapshot = new List<Channel<T>>(_channels);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            if (_channels.Count >= 50)
            {
                Parallel.ForEach(channelsSnapshot, channel => channel.Writer.TryComplete(error));
            }
            else
            {
                foreach (var channelsKey in channelsSnapshot)
                {
                    channelsKey.Writer.TryComplete(error);
                }
            }
        }
    }
}