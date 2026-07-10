namespace JLloyd.AsyncSources.Benchmarks;

[ShortRunJob]
[MemoryDiagnoser]
public class BroadcastBenchmarks
{
    private const int Unbounded = 0;

    private int[] _values = [];

    [Params(1, 16, 64)]
    public int Consumers { get; set; }

    [Params(256)]
    public int Items { get; set; }

    [Params(Unbounded, 1024)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _values = Enumerable.Range(0, Items).ToArray();
    }

    [Benchmark]
    public async Task<int> YieldSingleValue()
    {
        using var source = CreateSource();
        var consumers = await ConsumerGroup.Start(source, Consumers).ConfigureAwait(false);

        for (var value = 0; value < Items; value++)
        {
            await source.YieldReturn(value).ConfigureAwait(false);
        }

        source.Complete();
        return await consumers.VerifyAndGetTotal(ExpectedTotal()).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<int> YieldEnumerableBatch()
    {
        using var source = CreateSource();
        var consumers = await ConsumerGroup.Start(source, Consumers).ConfigureAwait(false);

        await source.YieldReturn(_values).ConfigureAwait(false);

        source.Complete();
        return await consumers.VerifyAndGetTotal(ExpectedTotal()).ConfigureAwait(false);
    }

    [Benchmark]
    public async Task<int> YieldAsyncEnumerableBatch()
    {
        using var source = CreateSource();
        var consumers = await ConsumerGroup.Start(source, Consumers).ConfigureAwait(false);

        await source.YieldReturn(GetValues()).ConfigureAwait(false);

        source.Complete();
        return await consumers.VerifyAndGetTotal(ExpectedTotal()).ConfigureAwait(false);
    }

    private AsyncEnumerableSource<int> CreateSource()
    {
        return Capacity == Unbounded
            ? new AsyncEnumerableSource<int>()
            : new AsyncEnumerableSource<int>(Capacity);
    }

    private int ExpectedTotal()
    {
        return Consumers * Items * (Items - 1) / 2;
    }

    private async IAsyncEnumerable<int> GetValues()
    {
        foreach (var value in _values)
        {
            yield return value;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}