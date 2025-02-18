using System;

namespace Duplicati.GUI.TrayIcon
{
    internal class ClickDebouncer
    {
        private DateTime _lastClickTime = DateTime.MinValue;
        private readonly TimeSpan _debounceInterval;

        public ClickDebouncer(TimeSpan? debounceInterval = null)
        {
            _debounceInterval = debounceInterval ?? TimeSpan.FromSeconds(1);
        }

        public bool ShouldProcessClick()
        {
            var now = DateTime.UtcNow;
            if (now - _lastClickTime < _debounceInterval)
            {
                return false;
            }
            
            _lastClickTime = now;
            return true;
        }
    }
}