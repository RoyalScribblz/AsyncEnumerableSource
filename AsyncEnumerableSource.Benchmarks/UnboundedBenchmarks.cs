namespace AsyncEnumerableSource.Benchmarks;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class UnboundedBenchmarks
{
    [Params(50, 1_000)]
    public int Elements { get; set; }
    
    [Params(1, 10, 100, 1_000, 10_000)]
    public int Consumers { get; set; }

    [Benchmark]
    public async Task YieldReturn()
    {
        var source = new AsyncEnumerableSource<int>();
        var tasks = new Task[Consumers];

        for (var i = 0; i < Consumers; i++)
        {
            tasks[i] = ConsumeAsyncEnumerable(source);
        }

        for (var i = 0; i < Elements; i++)
        {
            await source.YieldReturn(i);
        }
        
        source.Complete();
        await Task.WhenAll(tasks);
    }
    
    private static async Task<int> ConsumeAsyncEnumerable(AsyncEnumerableSource<int> source)
    {
        var sum = 0;
        
        await foreach (var item in source.GetAsyncEnumerable())
        {
            sum += item;
        }
        
        return sum;
    }
}