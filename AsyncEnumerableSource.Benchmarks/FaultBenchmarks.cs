namespace AsyncEnumerableSource.Benchmarks;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public static class FaultBenchmarks
{
    private static readonly Exception Exception = new();

    [Benchmark]
    public static async Task YieldReturn()
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
}