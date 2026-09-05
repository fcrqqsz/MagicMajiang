using MahjongGame.Core;
using MahjongGame.Core.Network;
using MahjongGame.Core.Network.Messages;

namespace MahjongGame.UI
{
    /// <summary>Owns the non-authoritative room AI draft until the user explicitly applies it.</summary>
    public sealed class AiQuickConfigController
    {
        private AiLoadoutTemplate? _pendingTemplate;
        private PlayerLoadoutMessage _pendingLoadout;

        public int SeatIndex { get; private set; } = -1;
        public bool IsAdding { get; private set; }
        public AiLoadoutDraft Draft { get; private set; }
        public bool HasPendingOverwrite => _pendingLoadout != null;

        public void Select(int seatIndex, bool isAdding, AiLoadoutDraft draft)
        {
            SeatIndex = seatIndex;
            IsAdding = isAdding;
            Draft = draft;
            CancelOverwrite();
        }

        public bool RequestTemplate(AiLoadoutTemplate template, PlayerLoadoutMessage loadout)
        {
            if (Draft == null || loadout == null) return false;
            if (Draft.IsDirty)
            {
                _pendingTemplate = template;
                _pendingLoadout = loadout;
                return false;
            }
            Draft.ReplaceLoadout(template, loadout);
            return true;
        }

        public bool ConfirmOverwrite()
        {
            if (Draft == null || !_pendingTemplate.HasValue || _pendingLoadout == null) return false;
            Draft.ReplaceLoadout(_pendingTemplate.Value, _pendingLoadout);
            CancelOverwrite();
            return true;
        }

        public void CancelOverwrite()
        {
            _pendingTemplate = null;
            _pendingLoadout = null;
        }

        public void AdoptAdvancedDraft(AiLoadoutDraft draft)
        {
            if (draft == null) return;
            Draft = draft.Clone();
            CancelOverwrite();
        }

        public void Clear()
        {
            SeatIndex = -1;
            IsAdding = false;
            Draft = null;
            CancelOverwrite();
        }
    }
}
