using AionDpsMeter.Core.Windowing;
using AionDpsMeter.Services.Services.Pets;
using AionDpsMeter.UI.Services.Windowing;
using Microsoft.AspNetCore.Components.Web;

namespace AionDpsMeter.UI.Pages
{
    partial class PetCollectionPage(PetCollectionTracker tracker, IWindowManagerService windowManager) : IDisposable
    {
        private const string AllGenera = "All";
        private static readonly string[] Genera = [AllGenera, "Fera", "Natura", "Cogni", "Varian", "Special"];

        private readonly CancellationTokenSource _cts = new();
        private PetCollectionView _view = new(PetCollectionStatus.Waiting, "", null, [], []);
        private int _version = -1;

        private string _search = string.Empty;
        private string _genus = AllGenera;
        private bool _hideMaxed;
        private bool _farmableOnly;
        private bool _sortByNeeded;

        private int Owned => _view.Pets.Count(p => p.Rank > 0);
        private int Maxed => _view.Pets.Count(p => p.Goal is null);

        private List<PetEntry> VisiblePets
        {
            get
            {
                var pets = _view.Pets.Where(p =>
                    (_genus == AllGenera || p.Genus == _genus)
                    && (!_hideMaxed || p.Goal is not null)
                    && (!_farmableOnly || p.Farmable)
                    && (_search.Length == 0 || p.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)));

                return _sortByNeeded
                    ? pets.OrderBy(p => p.Goal is null ? 1 : 0).ThenBy(p => Math.Max(0, (p.Goal ?? 0) - p.Souls)).ThenBy(p => p.Name).ToList()
                    : pets.OrderBy(p => p.Name).ToList();
            }
        }

        private string StatusText => _view.Status switch
        {
            PetCollectionStatus.Synced => $"Synced at login {_view.SyncedUtc?.ToLocalTime():HH:mm}",
            PetCollectionStatus.LastSession => $"Saved {_view.SyncedUtc?.ToLocalTime():d MMM HH:mm} + souls since",
            PetCollectionStatus.Partial => "Only counting souls since the meter started",
            _ => "Log into a character to load your collection",
        };

        private string StatusHint => _view.Status switch
        {
            PetCollectionStatus.Synced => "The game sent your whole collection when you logged in.",
            PetCollectionStatus.Waiting => "Start the meter before logging into your character: the game sends the collection at login.",
            _ => "The game only sends the whole collection at login. Go to character select and log back in to bring it up to date.",
        };

        protected override void OnInitialized()
        {
            Refresh();
            _ = PollAsync(_cts.Token);
        }

        private async Task PollAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    if (tracker.Version == _version) continue;
                    Refresh();
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void Refresh()
        {
            _version = tracker.Version;
            _view = tracker.GetView();
        }

        private static string CountText(int rank, int souls) => PetCollectionTracker.SoulsForNextRank(rank) is { } goal ? $"{souls}/{goal}" : "MAX";

        private static string RowHint(PetEntry pet) => pet.Goal switch
        {
            null => "Max rank",
            { } goal when pet.Souls >= goal && pet.Rank == 0 => "Enough souls to unlock",
            { } goal when pet.Souls >= goal => "Enough souls for the next rank",
            { } goal => $"{goal - pet.Souls} more for rank {pet.Rank + 1}",
        };

        private static double Percent(PetEntry pet) => pet.Goal is { } goal ? Math.Min(100, pet.Souls * 100.0 / goal) : 100;

        private static string GenusColor(string genus) => genus switch
        {
            "Fera" => "#E8794A",
            "Natura" => "#5BC17A",
            "Cogni" => "#5B8CFF",
            "Varian" => "#B07CF0",
            "Special" => "#E2B84B",
            _ => "#7C8494",
        };

        private void BeginDrag(MouseEventArgs _) => windowManager.Drag(WindowKey.PetCollection);

        private void CloseWindow() => windowManager.Close(WindowKey.PetCollection);

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
