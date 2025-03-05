namespace AsyncEnumerableSource.Benchmarks;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class AsyncEnumerableSourceBenchmarks
{
    private static readonly List<int> Data = Enumerable.Range(0, 1_000).ToList();

    [Benchmark]
    public async Task YieldReturn_SingleConsumer()
    {
        var source = new AsyncEnumerableSource<int>();
        var task = ConsumeAsyncEnumerable(source);

        foreach (var item in Data)
        {
            source.YieldReturn(item);
        }
        
        source.Complete();
        await task;
    }
    
    [Benchmark]
    [Arguments(1)]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1_000)]
    [Arguments(10_000)]
    public async Task YieldReturn_MultipleConsumers(int consumerCount)
    {
        var source = new AsyncEnumerableSource<int>();
        var tasks = Enumerable.Range(0, consumerCount).Select(_ => ConsumeAsyncEnumerable(source)).ToList();
        
        foreach (var item in Data)
        {
            source.YieldReturn(item);
        }
        
        source.Complete();
        await Task.WhenAll(tasks);
    }
    
    private async Task<List<int>> ConsumeAsyncEnumerable(AsyncEnumerableSource<int> source)
    {
        List<int> items = [];
        
        await foreach (var item in source.GetAsyncEnumerable())
        {
            items.Add(item);
        }
        
        return items;
    }
    
    [Benchmark]
    public async Task YieldReturn_WithCancellation()
    {
        var source = new AsyncEnumerableSource<int>();
        using var cts = new CancellationTokenSource();
        var task = ConsumeAsyncEnumerableWithCancellation(source, cts.Token);
        
        foreach (var item in Data.Take(500))
        {
            source.YieldReturn(item);
        }
        
        await cts.CancelAsync();
        
        foreach (var item in Data.Skip(500))
        {
            source.YieldReturn(item);
        }
        
        source.Complete();
        await task;
    }

    private async Task<List<int>> ConsumeAsyncEnumerableWithCancellation(AsyncEnumerableSource<int> source, CancellationToken token)
    {
        List<int> items = [];
        try
        {
            await foreach (var item in source.GetAsyncEnumerable(token))
            {
                items.Add(item);
            }
        }
        catch (OperationCanceledException)
        {
        }
        
        return items;
    }
    
    [Benchmark]
    public async Task Fault_WithException()
    {
        var source = new AsyncEnumerableSource<int>();
        var task = ConsumeAsyncEnumerableWithException(source);
        
        foreach (var item in Data.Take(500)) // Half the dataset
        {
            source.YieldReturn(item);
        }
        
        source.Fault(new Exception("Benchmark Exception"));
        await task;
    }

    private async Task<(List<int>, Exception?)> ConsumeAsyncEnumerableWithException(AsyncEnumerableSource<int> source)
    {
        List<int> items = [];
        Exception? exception = null;
        
        try
        {
            await foreach (var item in source.GetAsyncEnumerable())
            {
                items.Add(item);
            }
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        
        return (items, exception);
    }
}