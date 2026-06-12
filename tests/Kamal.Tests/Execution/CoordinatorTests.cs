using System.Collections.Concurrent;
using Kamal.Execution;

namespace Kamal.Tests.Execution;

[Collection("kamal-config")]
public sealed class CoordinatorTests : IDisposable
{
   public CoordinatorTests()
   {
      Coordinator.BackendFactory = host => new FakeBackend(host);
   }

   public void Dispose()
   {
      Coordinator.Reset();
   }

   [Fact]
   public async Task OnRunsWorkOnAllHosts()
   {
      var visited = new ConcurrentBag<string>();

      await Coordinator.On(["1.1.1.1", "1.1.1.2", "1.1.1.3"], backend =>
      {
         visited.Add(backend.Host);
         return Task.CompletedTask;
      });

      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3"], visited.Order());
   }

   [Fact]
   public async Task OnDeduplicatesHosts()
   {
      var visited = new ConcurrentBag<string>();

      await Coordinator.On(["1.1.1.1", "1.1.1.1", "1.1.1.2"], backend =>
      {
         visited.Add(backend.Host);
         return Task.CompletedTask;
      });

      Assert.Equal(2, visited.Count);
   }

   [Fact]
   public async Task OnAcceptsSingleHost()
   {
      var visited = new ConcurrentBag<string>();

      await Coordinator.On("1.1.1.1", backend =>
      {
         visited.Add(backend.Host);
         return Task.CompletedTask;
      });

      Assert.Equal("1.1.1.1", Assert.Single(visited));
   }

   [Fact]
   public async Task SingleFailureRethrowsExecuteErrorAfterAllHostsComplete()
   {
      var visited = new ConcurrentBag<string>();

      var error = await Assert.ThrowsAsync<ExecuteError>(() => Coordinator.On(["1.1.1.1", "1.1.1.2", "1.1.1.3"], async backend =>
      {
         if (backend.Host == "1.1.1.2")
            throw new ExecuteError(backend.Host, "command failed");

         await Task.Delay(50);
         visited.Add(backend.Host);
      }));

      Assert.Equal("1.1.1.2", error.Host);
      Assert.Equal(["1.1.1.1", "1.1.1.3"], visited.Order());
   }

   [Fact]
   public async Task MultipleFailuresAggregateIntoMultipleExecuteError()
   {
      var error = await Assert.ThrowsAsync<MultipleExecuteError>(() => Coordinator.On(["1.1.1.1", "1.1.1.2", "1.1.1.3"], backend =>
      {
         if (backend.Host != "1.1.1.3")
            throw new ExecuteError(backend.Host, $"failed on {backend.Host}");

         return Task.CompletedTask;
      }));

      Assert.Equal(2, error.Errors.Count);
      Assert.Contains("Exceptions on 2 hosts:", error.Message);
      Assert.Equal(["1.1.1.1", "1.1.1.2"], error.Errors.Select(e => e.Host).Order());
   }

   [Fact]
   public async Task NonExecuteErrorsAreWrappedWithTheHost()
   {
      var error = await Assert.ThrowsAsync<ExecuteError>(() => Coordinator.On(["1.1.1.1"], _ => throw new InvalidOperationException("kaboom")));

      Assert.Equal("1.1.1.1", error.Host);
      Assert.Contains("Exception while executing on host 1.1.1.1", error.Message);
      Assert.Contains("kaboom", error.Message);
   }

   [Fact]
   public async Task GroupsRunInBatchesOfLimit()
   {
      var order = new List<string>();
      var orderLock = new object();

      await Coordinator.On(["a", "b", "c", "d", "e"], limit: 2, waitSeconds: null, work: backend =>
      {
         lock (orderLock)
            order.Add(backend.Host);

         return Task.CompletedTask;
      });

      Assert.Equal(5, order.Count);
      Assert.Equal(["a", "b"], order.Take(2).Order());
      Assert.Equal(["c", "d"], order.Skip(2).Take(2).Order());
      Assert.Equal(["e"], order.Skip(4).ToList());
   }

   [Fact]
   public async Task GroupsWithoutLimitRunAsOneBatch()
   {
      var visited = new ConcurrentBag<string>();

      await Coordinator.On(["a", "b", "c"], limit: null, waitSeconds: null, work: backend =>
      {
         visited.Add(backend.Host);
         return Task.CompletedTask;
      });

      Assert.Equal(3, visited.Count);
   }

   [Fact]
   public async Task GroupFailureStopsSubsequentGroups()
   {
      var visited = new ConcurrentBag<string>();

      await Assert.ThrowsAsync<ExecuteError>(() => Coordinator.On(["a", "b", "c", "d"], limit: 2, waitSeconds: null, work: backend =>
      {
         visited.Add(backend.Host);

         if (backend.Host == "a")
            throw new ExecuteError(backend.Host, "boot failed");

         return Task.CompletedTask;
      }));

      Assert.Equal(["a", "b"], visited.Order());
   }

   [Fact]
   public async Task GroupsWaitBetweenBatches()
   {
      var started = DateTime.UtcNow;

      await Coordinator.On(["a", "b"], limit: 1, waitSeconds: 0.05, work: _ => Task.CompletedTask);

      Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(0.09));
   }

   [Fact]
   public async Task RunLocallyUsesTheLocalBackendFactory()
   {
      var fake = new FakeBackend("localhost");
      Coordinator.LocalBackendFactory = () => fake;

      await Coordinator.RunLocally(backend => backend.Execute(["echo", "hi"]));

      Assert.Equal("echo hi", Assert.Single(fake.Commands));
   }
}
