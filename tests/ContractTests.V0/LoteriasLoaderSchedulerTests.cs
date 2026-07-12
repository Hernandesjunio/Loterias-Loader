using Lotofacil.Loader.Application;
using Lotofacil.Loader.Domain;
using Xunit;

namespace ContractTests.V0;

public sealed class LoteriasLoaderSchedulerTests
{
    private static readonly IReadOnlyList<string> DefaultOrder =
    [
        LoteriaModalityKeys.Lotofacil,
        LoteriaModalityKeys.MegaSena,
        LoteriaModalityKeys.Quina
    ];

    [Fact]
    public async Task R1_round_robin_returns_lotofacil_mega_sena_quina()
    {
        var scheduler = CreateScheduler(out _);

        var modalities = await AcquireSequenceAsync(scheduler, count: 3);

        Assert.Equal(
            new[] { LoteriaModalityKeys.Lotofacil, LoteriaModalityKeys.MegaSena, LoteriaModalityKeys.Quina },
            modalities);
    }

    [Fact]
    public async Task R2_wrap_around_returns_lotofacil_on_fourth_acquire()
    {
        var scheduler = CreateScheduler(out _);

        await AcquireSequenceAsync(scheduler, count: 3);
        var fourth = await AcquireAndAdvanceAsync(scheduler);

        Assert.Equal(LoteriaModalityKeys.Lotofacil, fourth);
    }

    [Fact]
    public async Task R3_long_cycle_repeats_pattern_and_ends_at_index_zero()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        var scheduler = CreateScheduler(store);

        var modalities = await AcquireSequenceAsync(scheduler, count: 9);

        Assert.Equal(
            Enumerable.Repeat(
                new[]
                {
                    LoteriaModalityKeys.Lotofacil,
                    LoteriaModalityKeys.MegaSena,
                    LoteriaModalityKeys.Quina
                },
                3).SelectMany(x => x),
            modalities);

        Assert.Equal(0, store.CurrentState!.NextModalityIndex);
    }

    [Fact]
    public async Task R4_missing_row_starts_at_lotofacil_and_advances_to_index_one()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        var scheduler = CreateScheduler(store);

        var first = await scheduler.AcquireNextModalityAsync(CancellationToken.None);
        Assert.Equal(LoteriaModalityKeys.Lotofacil, first.ModalityKey);
        Assert.Equal(0, first.Index);

        await scheduler.AdvanceAfterAttemptAsync(first, CancellationToken.None);

        Assert.Equal(1, store.CurrentState!.NextModalityIndex);
    }

    [Fact]
    public async Task R5_advance_after_success_updates_last_modality_and_timestamp()
    {
        var clock = new FakeClock(ContractTestHarness.Utc("2026-07-12T15:00:00Z"));
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        var scheduler = new LoteriasModalityRotationScheduler(store, clock, DefaultOrder);

        var acquire = await scheduler.AcquireNextModalityAsync(CancellationToken.None);
        await scheduler.AdvanceAfterAttemptAsync(acquire, CancellationToken.None);

        Assert.Equal(1, store.CurrentState!.NextModalityIndex);
        Assert.Equal(LoteriaModalityKeys.Lotofacil, store.CurrentState.LastModalityKey);
        Assert.Equal(clock.UtcNow, store.CurrentState.LastRunUtc);
    }

    [Fact]
    public async Task R6_advance_after_failure_still_increments_index()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        var scheduler = CreateScheduler(store);

        var acquire = await scheduler.AcquireNextModalityAsync(CancellationToken.None);

        try
        {
            throw new InvalidOperationException("simulated failure");
        }
        catch (InvalidOperationException)
        {
            await scheduler.AdvanceAfterAttemptAsync(acquire, CancellationToken.None);
        }

        Assert.Equal(1, store.CurrentState!.NextModalityIndex);
    }

    [Fact]
    public async Task R7_custom_modality_order_is_respected()
    {
        var customOrder = new[]
        {
            LoteriaModalityKeys.Quina,
            LoteriaModalityKeys.Lotofacil,
            LoteriaModalityKeys.MegaSena
        };

        var scheduler = new LoteriasModalityRotationScheduler(
            new InMemoryLoteriasLoaderSchedulerStore(),
            new FakeClock(ContractTestHarness.Utc("2026-07-12T15:00:00Z")),
            customOrder);

        var modalities = await AcquireSequenceAsync(scheduler, count: 3);

        Assert.Equal(customOrder, modalities);
    }

    [Fact]
    public async Task R8_stale_etag_write_throws_scheduler_concurrency_exception()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        store.Seed(new LoteriasLoaderSchedulerState(0, null, null, "etag-1"));
        var scheduler = CreateScheduler(store);

        var acquire = await scheduler.AcquireNextModalityAsync(CancellationToken.None);
        store.Seed(store.CurrentState! with { ETag = "etag-other" });

        await Assert.ThrowsAsync<SchedulerConcurrencyException>(() =>
            scheduler.AdvanceAfterAttemptAsync(acquire, CancellationToken.None));
    }

    [Fact]
    public async Task R9_out_of_range_index_normalizes_to_zero()
    {
        var store = new InMemoryLoteriasLoaderSchedulerStore();
        store.Seed(new LoteriasLoaderSchedulerState(99, null, null, null));
        var scheduler = CreateScheduler(store);

        var acquire = await scheduler.AcquireNextModalityAsync(CancellationToken.None);

        Assert.Equal(LoteriaModalityKeys.Lotofacil, acquire.ModalityKey);
        Assert.Equal(0, acquire.Index);
    }

    private static LoteriasModalityRotationScheduler CreateScheduler(
        out InMemoryLoteriasLoaderSchedulerStore store) =>
        CreateScheduler(store = new InMemoryLoteriasLoaderSchedulerStore());

    private static LoteriasModalityRotationScheduler CreateScheduler(
        InMemoryLoteriasLoaderSchedulerStore store) =>
        new(
            store,
            new FakeClock(ContractTestHarness.Utc("2026-07-12T15:00:00Z")),
            DefaultOrder);

    private static async Task<IReadOnlyList<string>> AcquireSequenceAsync(
        ILoteriasLoaderScheduler scheduler,
        int count)
    {
        var modalities = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            modalities.Add(await AcquireAndAdvanceAsync(scheduler));
        }

        return modalities;
    }

    private static async Task<string> AcquireAndAdvanceAsync(ILoteriasLoaderScheduler scheduler)
    {
        var acquire = await scheduler.AcquireNextModalityAsync(CancellationToken.None);
        await scheduler.AdvanceAfterAttemptAsync(acquire, CancellationToken.None);
        return acquire.ModalityKey;
    }
}
