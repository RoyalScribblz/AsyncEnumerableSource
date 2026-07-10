namespace JLloyd.AsyncSources.Benchmarks;

internal sealed class ConsumerGroup
{
    private readonly Task<int>[] _tasks;

    private ConsumerGroup(Task<int>[] tasks)
    {
        _tasks = tasks;
    }

    public static Task<ConsumerGroup> Start(
        AsyncEnumerableSource<int> source,
        int count,
        CancellationToken ct = default)
    {
        return StartCore(source, count, captureFault: false, ct);
    }

    public static Task<ConsumerGroup> StartFaulting(
        AsyncEnumerableSource<int> source,
        int count,
        CancellationToken ct = default)
    {
        return StartCore(source, count, captureFault: true, ct);
    }

    public async Task<int> VerifyAndGetTotal(int expected)
    {
        var results = await Task.WhenAll(_tasks).ConfigureAwait(false);
        var total = 0;

        for (var index = 0; index < results.Length; index++)
        {
            total += results[index];
        }

        if (total != expected)
        {
            throw new InvalidOperationException($"Expected {expected}, got {total}.");
        }

        return total;
    }

    private static async Task<ConsumerGroup> StartCore(
        AsyncEnumerableSource<int> source,
        int count,
        bool captureFault,
        CancellationToken ct)
    {
        var started = new TaskCompletionSource[count];
        var tasks = new Task<int>[count];

        for (var index = 0; index < count; index++)
        {
            started[index] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tasks[index] = Consume(source, started[index], captureFault, ct);
        }

        for (var index = 0; index < started.Length; index++)
        {
            await started[index].Task.ConfigureAwait(false);
        }

        return new ConsumerGroup(tasks);
    }

    private static async Task<int> Consume(
        AsyncEnumerableSource<int> source,
        TaskCompletionSource started,
        bool captureFault,
        CancellationToken ct)
    {
        var sum = 0;
        await using var enumerator = source.GetAsyncEnumerable(ct).GetAsyncEnumerator(ct);

        var moveNext = enumerator.MoveNextAsync();
        started.SetResult();

        try
        {
            while (await moveNext.ConfigureAwait(false))
            {
                sum += enumerator.Current;
                moveNext = enumerator.MoveNextAsync();
            }
        }
        catch (Exception) when (captureFault)
        {
            return 1;
        }

        return sum;
    }
}