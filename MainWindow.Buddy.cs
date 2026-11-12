using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DynamicIsland;

/// <summary>
/// The character in the open menu: it watches what you do. Buttons make it hop, switching a page
/// makes it bounce and look, turning something on makes it happy, typing makes it type along.
/// Poke any character and it shows one of seven emotions.
/// </summary>
public partial class MainWindow
{
    private DateTime _menuTypedAt;

    private Views.CharacterMood? MenuBuddyMood() =>
        DateTime.Now - _menuTypedAt < TimeSpan.FromMilliseconds(700) ? Views.CharacterMood.Typing : null;

    private void InitBuddyReactions()
    {
        ExpandedView.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            if (!MenuBuddy.IsVisible || e.OriginalSource is not DependencyObject d) return;
            if (FindUp<ButtonBase>(d) is { } b && b is not ToggleButton) MenuBuddy.Hop(0.45);
        }), true);

        ExpandedView.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, e) =>
        {
            if (_settingsLoading || !MenuBuddy.IsVisible || !Island.IsMouseOver) return;
            if (e.OriginalSource is RadioButton { GroupName: "Tabs" }) MenuBuddy.Hop(0.9);   // new page: bounce
            else if (e.OriginalSource is CheckBox) MenuBuddy.Cheer();                        // switched on: yay
            else MenuBuddy.Hop(0.5);
        }), true);

        ExpandedView.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, e) =>
        {
            if (_settingsLoading || !MenuBuddy.IsVisible || !Island.IsMouseOver) return;
            if (e.OriginalSource is CheckBox) MenuBuddy.Hop(0.35);
        }), true);

        ExpandedView.AddHandler(PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase)
            {
                _menuTypedAt = DateTime.Now;
                if (e.Key == Key.Enter) MenuBuddy.Hop(0.7);
            }
        }), true);
    }

    private static T? FindUp<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Was the click on a character? Then it reacts instead of the island.</summary>
    private bool TryEmote(MouseButtonEventArgs e)
    {
        foreach (var ch in new[] { Character, MenuBuddy, ChatBuddy })
        {
            if (!ch.IsVisible) continue;
            var p = e.GetPosition(ch);
            if (p.X < -4 || p.Y < -4 || p.X > ch.ActualWidth + 4 || p.Y > ch.ActualHeight + 4) continue;
            ch.Emote();
            Kick(1.2);
            return true;
        }
        return false;
    }
}
