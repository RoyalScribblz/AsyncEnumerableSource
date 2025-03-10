namespace AsyncEnumerableSource.Benchmarks;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public static class CancellationBenchmarks
{
    [Benchmark]
    public static async Task YieldReturn()
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