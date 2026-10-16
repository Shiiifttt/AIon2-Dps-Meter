using AionDpsMeter.UI.ViewModels;
using Microsoft.AspNetCore.Components;

namespace AionDpsMeter.UI.Pages
{
    /// <summary>
    /// Smallest layout: one-line header (boss, HP %, time, party DPS), a thin HP bar and
    /// 16px player rows with only name, damage share and DPS. Buttons appear on header hover.
    /// </summary>
    public partial class MainDpsUltraCompact : ComponentBase
    {

        [Parameter]
        public MainDpsViewModel? ViewModel { get; set; }

    }
}
