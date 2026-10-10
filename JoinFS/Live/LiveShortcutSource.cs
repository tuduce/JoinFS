using System;
using System.Collections.Generic;
using JoinFS.Properties;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.Live
{
    /// <summary>
    /// The keyboard shortcuts, kept in the same Settings.Default as the old Shortcuts window, so the old and new UI see each other's
    /// choices. The keys are asked of the system with <see cref="Shortcut"/>, as MainForm does, so they work while the simulator has the focus.
    /// </summary>
    class LiveShortcutSource : IShortcutSource
    {
        static readonly ShortcutAction[] Actions = Enum.GetValues<ShortcutAction>();

        readonly Shortcut[] shortcuts = new Shortcut[Actions.Length];

        public LiveShortcutSource()
        {
            for (int index = 0; index < Actions.Length; index++)
            {
                shortcuts[index] = new Shortcut();
                shortcuts[index].Load(Combination(Actions[index]));
            }
        }

        public IReadOnlyList<ShortcutBinding> Load()
        {
            List<ShortcutBinding> bindings = [];
            foreach (ShortcutAction action in Actions)
            {
                bindings.Add(new ShortcutBinding(action, Enabled(action), Combination(action)));
            }
            return bindings;
        }

        public void Save(ShortcutBinding binding)
        {
            // a combination the old window could not have made is not kept
            if (Shortcut.IsValid(binding.Combination))
            {
                SetCombination(binding.Action, binding.Combination);
                shortcuts[(int)binding.Action].Load(binding.Combination);
            }
            SetEnabled(binding.Action, binding.Enabled);
            Settings.Default.Save();
        }

        public IReadOnlyList<ShortcutAction> TakePressed()
        {
            bool control = Shortcut.KeyPressed(Shortcut.VK_CONTROL);
            bool shift = Shortcut.KeyPressed(Shortcut.VK_SHIFT);
            bool alt = Shortcut.KeyPressed(Shortcut.VK_ALT);

            List<ShortcutAction> pressed = [];
            foreach (ShortcutAction action in Actions)
            {
                // every shortcut keeps track of its key, so one that is turned on while its key is held does not fire at once
                if (shortcuts[(int)action].Pressed(control, shift, alt) && Enabled(action))
                {
                    pressed.Add(action);
                }
            }
            return pressed;
        }

        static bool Enabled(ShortcutAction action) => action switch
        {
            ShortcutAction.Network => Settings.Default.ShortcutNetwork,
            ShortcutAction.Simulator => Settings.Default.ShortcutSimulator,
            ShortcutAction.AllowShared => Settings.Default.ShortcutAllowShared,
            ShortcutAction.HandOver => Settings.Default.ShortcutHandOver,
            ShortcutAction.EnterCockpit => Settings.Default.ShortcutEnter,
            ShortcutAction.Follow => Settings.Default.ShortcutFollow,
            ShortcutAction.Record => Settings.Default.ShortcutRecord,
            ShortcutAction.Overdub => Settings.Default.ShortcutOverdub,
            ShortcutAction.Stop => Settings.Default.ShortcutStop,
            _ => Settings.Default.ShortcutReplay,
        };

        static void SetEnabled(ShortcutAction action, bool enabled)
        {
            switch (action)
            {
                case ShortcutAction.Network: Settings.Default.ShortcutNetwork = enabled; break;
                case ShortcutAction.Simulator: Settings.Default.ShortcutSimulator = enabled; break;
                case ShortcutAction.AllowShared: Settings.Default.ShortcutAllowShared = enabled; break;
                case ShortcutAction.HandOver: Settings.Default.ShortcutHandOver = enabled; break;
                case ShortcutAction.EnterCockpit: Settings.Default.ShortcutEnter = enabled; break;
                case ShortcutAction.Follow: Settings.Default.ShortcutFollow = enabled; break;
                case ShortcutAction.Record: Settings.Default.ShortcutRecord = enabled; break;
                case ShortcutAction.Overdub: Settings.Default.ShortcutOverdub = enabled; break;
                case ShortcutAction.Stop: Settings.Default.ShortcutStop = enabled; break;
                default: Settings.Default.ShortcutReplay = enabled; break;
            }
        }

        static string Combination(ShortcutAction action) => action switch
        {
            ShortcutAction.Network => Settings.Default.ShortcutNetworkKey,
            ShortcutAction.Simulator => Settings.Default.ShortcutSimulatorKey,
            ShortcutAction.AllowShared => Settings.Default.ShortcutAllowSharedKey,
            ShortcutAction.HandOver => Settings.Default.ShortcutHandOverKey,
            ShortcutAction.EnterCockpit => Settings.Default.ShortcutEnterKey,
            ShortcutAction.Follow => Settings.Default.ShortcutFollowKey,
            ShortcutAction.Record => Settings.Default.ShortcutRecordKey,
            ShortcutAction.Overdub => Settings.Default.ShortcutOverdubKey,
            ShortcutAction.Stop => Settings.Default.ShortcutStopKey,
            _ => Settings.Default.ShortcutReplayKey,
        };

        static void SetCombination(ShortcutAction action, string combination)
        {
            switch (action)
            {
                case ShortcutAction.Network: Settings.Default.ShortcutNetworkKey = combination; break;
                case ShortcutAction.Simulator: Settings.Default.ShortcutSimulatorKey = combination; break;
                case ShortcutAction.AllowShared: Settings.Default.ShortcutAllowSharedKey = combination; break;
                case ShortcutAction.HandOver: Settings.Default.ShortcutHandOverKey = combination; break;
                case ShortcutAction.EnterCockpit: Settings.Default.ShortcutEnterKey = combination; break;
                case ShortcutAction.Follow: Settings.Default.ShortcutFollowKey = combination; break;
                case ShortcutAction.Record: Settings.Default.ShortcutRecordKey = combination; break;
                case ShortcutAction.Overdub: Settings.Default.ShortcutOverdubKey = combination; break;
                case ShortcutAction.Stop: Settings.Default.ShortcutStopKey = combination; break;
                default: Settings.Default.ShortcutReplayKey = combination; break;
            }
        }
    }
}
