using AionDpsMeter.UI.ViewModels;
using Microsoft.AspNetCore.Components;

namespace AionDpsMeter.UI.Pages
{
    /// <summary>
    /// Dungeon layout: up to 5 players side by side, each in an equal share of the width.
    /// Players keep their slot instead of being re-sorted by damage, so nobody jumps around.
    /// </summary>
    public partial class MainDpsHorizontal : ComponentBase
    {
        private const int MaxSlots = 5;

        [Parameter]
        public MainDpsViewModel? ViewModel { get; set; }

        // Player id per slot, left to right.
        private readonly List<long> _slotIds = [];

        private List<PlayerRenderState> GetSlotPlayers()
        {
            // ViewModel.Players is sorted by damage, so this picks the top 5 when there are more.
            var shown = ViewModel!.Players.Take(MaxSlots).ToDictionary(p => p.PlayerId);

            // A player who dropped out of the top 5 (or left) frees their slot for a newcomer, in place.
            var newcomers = new Queue<long>(shown.Keys.Where(id => !_slotIds.Contains(id)));
            for (var i = 0; i < _slotIds.Count; i++)
            {
                if (!shown.ContainsKey(_slotIds[i]))
                    _slotIds[i] = newcomers.Count > 0 ? newcomers.Dequeue() : 0;
            }
            _slotIds.RemoveAll(id => id == 0);
            _slotIds.AddRange(newcomers);

            return _slotIds.Select(id => shown[id]).ToList();
        }
    }
}
