namespace AsyncEnumerableSource.UnitTests;

public class TestTest
{
    private static readonly Exception Exception = new();

    [Fact]
    public async Task YieldReturnA()
    {
        var source = new AsyncEnumerableSource<int>();
        var task = ConsumeAsyncEnumerableWithException(source);

        source.Fault(Exception);
        await task;
    }
    
    private static async Task<(int, Exception?)> ConsumeAsyncEnumerableWithException(AsyncEnumerableSource<int> source)
    {
        var sum = 0;
        Exception? exception = null;
        
        try
        {
            await foreach (var item in source.GetAsyncEnumerable())
            {
                sum += item;
            }
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        return (sum, exception);
    }

    [Fact]
    public async Task YieldReturnB()
    {
        using var cts = new CancellationTokenSource();
        var source = new AsyncEnumerableSource<int>();
        var task = ConsumeAsyncEnumerableWithCancellation(source, cts.Token);

        await cts.CancelAsync();
        await task;
    }
    
    private static async Task<(int, Exception?)> ConsumeAsyncEnumerableWithCancellation(
        AsyncEnumerableSource<int> source,
        CancellationToken cancellationToken)
    {
        var sum = 0;
        Exception? exception = null;
        
        try
        {
            await foreach (var item in source.GetAsyncEnumerable(cancellationToken))
            {
                sum += item;
            }
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        return (sum, exception);
    }
}