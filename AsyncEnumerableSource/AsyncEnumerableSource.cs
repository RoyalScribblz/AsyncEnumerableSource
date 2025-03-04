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

public class AsyncEnumerableSource<T> : AsyncEnumerableSource
{
    private readonly List<Channel<T>> _channels = [];
    private bool _completed;
    private Exception? _exception;
    private readonly ReaderWriterLockSlim _lock = new();

    public async IAsyncEnumerable<T> GetAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<T> channel;
        
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_exception != null)
            {
                throw _exception;
            }

            if (_completed)
            {
                yield break;
            }

            _lock.EnterWriteLock();
            try
            {
                channel = Channel.CreateUnbounded<T>(ChannelOptions);
                _channels.Add(channel);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
        
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
        List<Channel<T>> channelsSnapshot;
        
        _lock.EnterReadLock();
        try
        {
            if (_completed)
            {
                return;
            }

            channelsSnapshot = [.._channels];
        }
        finally
        {
            _lock.ExitReadLock();
        }

        var writeTasks = channelsSnapshot
            .Select(channel => Task.Run(() => { channel.Writer.TryWrite(value); }));
        
        await Task.WhenAll(writeTasks).ConfigureAwait(false);
    }

    public void Complete()
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_completed)
            {
                return;
            }
            
            foreach (var channel in _channels)
            {
                channel.Writer.TryComplete();
            }
            
            _lock.EnterWriteLock();
            try
            {
                _completed = true;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    public void Fault(Exception error)
    {
        _lock.EnterUpgradeableReadLock();
        try
        {
            if (_completed)
            {
                return;
            }
            
            foreach (var channel in _channels)
            {
                channel.Writer.TryComplete(error);
            }
            
            _lock.EnterWriteLock();
            try
            {
                _completed = true;
                _exception = error;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }
}