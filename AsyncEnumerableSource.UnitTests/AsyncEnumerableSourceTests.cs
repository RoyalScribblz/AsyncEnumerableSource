namespace AsyncEnumerableSource.UnitTests;

public sealed class AsyncEnumerableSourceTests
{
    [Fact]
    public async Task AsyncEnumerable_From_Source_Should_Yield_Items_Yielded_On_Source()
    {
        // Arrange
        var expected = Enumerable.Range(0, 5).ToList();
        
        var source = new AsyncEnumerableSource<int>();

        var task = GetItemsFromAsyncEnumerable();
        
        async Task<List<int>> GetItemsFromAsyncEnumerable()
        {
            List<int> items = [];
            
            await foreach (var item in source.GetAsyncEnumerable())
            {
                items.Add(item);
            }
            
            return items;
        }

        // Act
        foreach (var item in expected)
        {
            await source.YieldReturn(item);
        }
        
        source.Complete();
        
        var result = await task;

        // Assert
        result.Should().BeEquivalentTo(expected);
    }
    
    [Fact]
    public async Task Multiple_AsyncEnumerables_From_Source_Should_Yield_Same_Items_Yielded_On_Source()
    {
        // Arrange
        var expected = Enumerable.Range(0, 5).ToList();
        
        var source = new AsyncEnumerableSource<int>();

        List<Task<List<int>>> tasks = [];

        for (var i = 0; i < 5; i++)
        {
            tasks.Add(GetItemsFromAsyncEnumerable());
        }
        
        async Task<List<int>> GetItemsFromAsyncEnumerable()
        {
            List<int> items = [];
            
            await foreach (var item in source.GetAsyncEnumerable())
            {
                items.Add(item);
            }
            
            return items;
        }

        // Act
        foreach (var item in expected)
        {
            await source.YieldReturn(item);
        }
        
        source.Complete();

        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var result in results)
        {
            result.Should().BeEquivalentTo(expected);
        }
    }
    
    [Fact]
    public async Task AsyncEnumerable_From_Source_Should_Throw_Exception_Faulted_On_Source()
    {
        // Arrange
        var expected = Enumerable.Range(0, 5).ToList();
        var expectedException = new Exception("Test-Error");
        
        var source = new AsyncEnumerableSource<int>();

        var task = GetItemsAndExceptionFromAsyncEnumerable();
        
        async Task<(List<int> Items, Exception? Exception)> GetItemsAndExceptionFromAsyncEnumerable()
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

        // Act
        foreach (var item in expected)
        {
            await source.YieldReturn(item);
        }
        
        source.Fault(expectedException);

        var result = await task;

        // Assert
        result.Items.Should().BeEquivalentTo(expected);
        result.Exception.Should().BeEquivalentTo(expectedException);
    }
    
    [Fact]
    public async Task Multiple_AsyncEnumerables_From_Source_Should_Throw_Exception_Faulted_On_Source()
    {
        // Arrange
        var expected = Enumerable.Range(0, 5).ToList();
        var expectedException = new Exception("Test-Error");
        
        var source = new AsyncEnumerableSource<int>();

        var tasks = GetItemsAndExceptionFromAsyncEnumerable();
        
        async Task<(List<int> Items, Exception? Exception)> GetItemsAndExceptionFromAsyncEnumerable()
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

        // Act
        foreach (var item in expected)
        {
            await source.YieldReturn(item);
        }
        
        source.Fault(expectedException);

        var results = await Task.WhenAll(tasks);

        // Assert
        foreach (var result in results)
        {
            result.Items.Should().BeEquivalentTo(expected);
            result.Exception.Should().BeEquivalentTo(expectedException);
        }
    }
    
    [Fact]
    public async Task AsyncEnumerable_From_Source_Should_Stop_Yielding_When_Cancelled()
    {
        // Arrange
        var expected = Enumerable.Range(0, 5).ToList();
        var cts = new CancellationTokenSource();
        
        var source = new AsyncEnumerableSource<int>();

        var task = GetItemsFromAsyncEnumerable();
        var cancelledTask = GetItemsFromAsyncEnumerable(cts.Token);
        
        async Task<List<int>> GetItemsFromAsyncEnumerable(CancellationToken cancellationToken = default)
        {
            List<int> items = [];

            try
            {
                await foreach (var item in source.GetAsyncEnumerable(cancellationToken)) // TODO can you annotate with exception?
                {
                    items.Add(item);
                }
            }
            catch (OperationCanceledException)
            {
            }
            
            return items;
        }

        // Act
        foreach (var item in expected[..3])
        {
            await source.YieldReturn(item);
        }

        await Task.Delay(2);  // Allow time for channel to emit previous values before the task is cancelled
        await cts.CancelAsync();

        foreach (var item in expected[3..])
        {
            await source.YieldReturn(item);
        }
        
        source.Complete();

        var result = await task;
        var cancelledResult = await cancelledTask;

        // Assert
        result.Should().BeEquivalentTo(expected);
        cancelledResult.Should().BeEquivalentTo(expected[..3]);
    }
    
    [Fact]
    public async Task AsyncEnumerable_From_Source_Should_Not_Yield_When_Source_Completed()
    {
        // Arrange
        var expected = Enumerable.Range(0, 5).ToList();
        var yieldedAfterComplete = Random.Shared.Next(expected.Max(), int.MaxValue);
        
        var source = new AsyncEnumerableSource<int>();

        var task = GetItemsFromAsyncEnumerable();
        
        async Task<List<int>> GetItemsFromAsyncEnumerable()
        {
            List<int> items = [];
            
            await foreach (var item in source.GetAsyncEnumerable())
            {
                items.Add(item);
            }
            
            return items;
        }

        // Act
        foreach (var item in expected)
        {
            await source.YieldReturn(item);
        }
        
        source.Complete();

        await source.YieldReturn(yieldedAfterComplete);
        
        var result = await task;

        // Assert
        result.Should().BeEquivalentTo(expected);
        result.Should().NotContain(yieldedAfterComplete);
    }
}