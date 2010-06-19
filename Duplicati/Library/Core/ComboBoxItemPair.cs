using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Core
{
    /// <summary>
    /// Helper class to keep track of items in a combobox
    /// </summary>
    /// <typeparam name="T">The type of item to keep</typeparam>
    public class ComboBoxItemPair<T>
    {
        private T m_value;
        private string m_display;

        /// <summary>
        /// The item to be stored in a combobox
        /// </summary>
        /// <param name="display">The string to display for the item</param>
        /// <param name="value">The value to store for the item</param>
        public ComboBoxItemPair(string display, T value)
        {
            m_value = value;
            m_display = display;
        }

        /// <summary>
        /// Gets the display value for the item
        /// </summary>
        /// <returns>The display value for the item</returns>
        public override string ToString()
        {
            return m_display ?? "";
        }

        /// <summary>
        /// Gets the value stored in the instance
        /// </summary>
        public T Value { get { return m_value; } }
    }
}
