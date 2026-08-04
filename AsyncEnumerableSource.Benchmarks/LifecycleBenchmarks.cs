namespace JLloyd.AsyncSources.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class ConsumerLifecycleBenchmarks
{
    private static readonly InvalidOperationException FaultException = new("Benchmark fault.");

    [Params(1, 16, 64)]
    public int Consumers { get; set; }

    [Benchmark]
    public async Task<int> CompleteActiveConsumers()
    {
        using var source = new AsyncEnumerableSource<int>();
        var consumers = await ConsumerGroup.Start(source, Consumers).ConfigureAwait(false);

        source.Complete();

        return await consumers.VerifyAndGetTotal(0).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<int> FaultActiveConsumers()
    {
        using var source = new AsyncEnumerableSource<int>();
        var consumers = await ConsumerGroup.StartFaulting(source, Consumers).ConfigureAwait(false);

        source.Fault(FaultException);

        var observedFaults = await consumers.VerifyAndGetTotal(Consumers).ConfigureAwait(false);
        return observedFaults;
    }
}

[ShortRunJob]
[MemoryDiagnoser]
public class EnumerationLifecycleBenchmarks
{
    [Benchmark]
    public async Task<int> CompletedSource()
    {
        using var source = new AsyncEnumerableSource<int>();
        source.Complete();

        var count = 0;
        await foreach (var value in source.GetAsyncEnumerable().ConfigureAwait(false))
        {
            count += value;
        }

        return count;
    }
}

[ShortRunJob]
[MemoryDiagnoser]
public class CancellationLifecycleBenchmarks
{
    [Benchmark]
    public async Task<int> CancelActiveConsumer()
    {
        using var source = new AsyncEnumerableSource<int>();
        using var cts = new CancellationTokenSource();
        var consumer = await ConsumerGroup.Start(source, 1, cts.Token).ConfigureAwait(false);

        await cts.CancelAsync().ConfigureAwait(false);
        await source.YieldReturn(1).ConfigureAwait(false);

        source.Complete();
        return await consumer.VerifyAndGetTotal(0).ConfigureAwait(false);
    }
}