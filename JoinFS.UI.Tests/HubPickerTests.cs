using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoinFS.UI.Models;
using JoinFS.UI.ViewModels;
using JoinFS.UI.Views;

namespace JoinFS.UI.Tests;

/// <summary>
/// The hub picker is a box the user can type in: an address book entry, the mesh code of another JoinFS, [ipv6]:port, ipv4:port or
/// name:port. The entries that match the text are the suggestions.
/// </summary>
public class HubPickerTests
{
    private static AddressBookViewModel Book(Rig rig) => rig.Main.AddressBook;

    [Fact]
    public void The_box_starts_with_the_pick()
    {
        Rig rig = new();
        rig.Main.AddressBook.Select("AirSherpa");

        Assert.Equal("AirSherpa", Book(rig).PickerText);
        Assert.Equal("AirSherpa", Book(rig).JoinTarget?.Name);
    }

    [Theory]
    [InlineData("12345 67890")] // the mesh code of another JoinFS
    [InlineData("[2001:db8::1]:7777")]
    [InlineData("203.0.113.9:7777")]
    [InlineData("hub.example.org:7777")]
    public void Typed_text_is_joined_as_it_is(string text)
    {
        Rig rig = new();

        Book(rig).PickerText = $"  {text} ";

        AddressBookEntry? target = Book(rig).JoinTarget;
        Assert.Equal(text, target?.Name);
        Assert.Equal(text, target?.Address);
        Assert.False(target?.BuiltIn);
    }

    [Fact]
    public void The_name_of_an_entry_in_any_case_joins_the_entry()
    {
        Rig rig = new();
        AddressBookRow entry = Book(rig).Entries.Single(e => e.Name == "AirSherpa");

        Book(rig).PickerText = "airsherpa";

        Assert.Same(entry.Entry, Book(rig).JoinTarget);
    }

    [Fact]
    public void A_blank_box_joins_nothing_and_goes_back_to_the_pick_when_left()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");

        Book(rig).PickerText = "  ";
        Assert.Null(Book(rig).JoinTarget);

        Book(rig).RestoreText();
        Assert.Equal("AirSherpa", Book(rig).PickerText);
    }

    [Fact]
    public void Typing_does_not_change_the_stored_pick()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");

        Book(rig).PickerText = "203.0.113.9:7777";

        Assert.Equal("AirSherpa", rig.Services.AddressBook.Load().SelectedName);
    }

    [Theory]
    [InlineData("sherpa", "AirSherpa")] // part of the name, any case
    [InlineData("", "AirSherpa")] // nothing typed: everything
    public void The_suggestions_are_the_entries_that_match_the_text(string text, string expected)
    {
        Rig rig = new();

        string[] matches = [.. Book(rig).Entries.Where(e => Book(rig).Offers(e, text)).Select(e => e.Name)];

        Assert.Contains(expected, matches);
        if (text.Length > 0)
            Assert.True(matches.Length < Book(rig).Entries.Count);
    }

    [Fact]
    public void An_entry_is_also_found_by_its_address()
    {
        Rig rig = new();
        AddressBookRow row = new(new AddressBookEntry("Friends", "203.0.113.9:7777"), _ => { });

        Assert.True(Book(rig).Offers(row, "113.9"));
        Assert.False(Book(rig).Offers(row, "198.51"));
    }

    [Fact]
    public void The_text_of_the_pick_as_it_was_put_there_offers_every_entry()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");

        Assert.All(Book(rig).Entries, e => Assert.True(Book(rig).Offers(e, "AirSherpa")));
        Assert.Contains(Book(rig).Entries, e => !Book(rig).Offers(e, "AirSherpa "));
    }

    [Fact]
    public async Task The_network_button_joins_the_typed_text_and_the_box_keeps_it_as_the_joined_hub()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");
        Book(rig).PickerText = "[2001:db8::1]:7777";

        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);

        Assert.True(rig.Main.Network.IsConnected);
        Assert.Equal("[2001:db8::1]:7777", Book(rig).PickerText);
        Assert.Equal("[2001:db8::1]:7777", Book(rig).TransientLabel);
        Assert.DoesNotContain(Book(rig).Entries, e => e.Name == "[2001:db8::1]:7777"); // not added to the book
    }

    [Fact]
    public async Task After_leaving_the_box_goes_back_to_the_pick_but_keeps_what_was_typed_over_the_joined_hub()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");
        Book(rig).PickerText = "203.0.113.9:7777";
        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);
        rig.Main.Poll();

        Book(rig).PickerText = "hub.example.org:7777"; // prepared while still joined
        await rig.Main.Network.ToggleCommand.ExecuteAsync(null); // leave
        rig.Main.Poll();

        Assert.Null(Book(rig).TransientLabel);
        Assert.Equal("hub.example.org:7777", Book(rig).PickerText);
        Assert.Equal("AirSherpa", Book(rig).Selected?.Name);
    }

    [Fact]
    public async Task After_leaving_an_untouched_box_shows_the_pick_again()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");
        Book(rig).PickerText = "203.0.113.9:7777";
        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);
        rig.Main.Poll();

        await rig.Main.Network.ToggleCommand.ExecuteAsync(null);
        rig.Main.Poll();

        Assert.Equal("AirSherpa", Book(rig).PickerText);
    }

    [AvaloniaFact]
    public void Typing_in_the_box_sets_the_text_and_narrows_the_list()
    {
        Rig rig = new();
        rig.Main.IsExpanded = true;
        MainWindow window = new() { DataContext = rig.Main, Width = 1200, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        AutoCompleteBox box = window.GetVisualDescendants().OfType<AutoCompleteBox>().First(c => c.IsEffectivelyVisible);
        TextBox input = box.GetVisualDescendants().OfType<TextBox>().First();
        input.Focus();
        input.SelectAll();
        window.KeyTextInput("sherpa");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("sherpa", Book(rig).PickerText);
        Assert.True(box.IsDropDownOpen);
        string[] shown = Offered(box);
        Assert.Contains("AirSherpa", shown);
        Assert.True(shown.Length < Book(rig).Entries.Count, string.Join(", ", shown));
        window.Close();
    }

    private static (MainWindow Window, AutoCompleteBox Box, TextBox Input) Show(Rig rig)
    {
        rig.Main.IsExpanded = true;
        MainWindow window = new() { DataContext = rig.Main, Width = 1200, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AutoCompleteBox box = window.GetVisualDescendants().OfType<AutoCompleteBox>().First(c => c.IsEffectivelyVisible);
        return (window, box, box.GetVisualDescendants().OfType<TextBox>().First());
    }

    private static string[] Offered(AutoCompleteBox box)
    {
        dynamic adapter = typeof(AutoCompleteBox)
            .GetProperty("SelectionAdapter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(box)!;
        return [.. ((System.Collections.IEnumerable)adapter.ItemsSource).Cast<AddressBookRow>().Select(r => r.Name)];
    }

    [AvaloniaFact]
    public void Clicking_the_box_opens_the_whole_list_and_picking_an_entry_puts_its_name_in_the_box()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");
        var (window, box, input) = Show(rig);

        input.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("AirSherpa", input.SelectedText); // focusing selects the text
        window.MouseDown(input.TranslatePoint(new Point(5, 5), window)!.Value, Avalonia.Input.MouseButton.Left);
        window.MouseUp(input.TranslatePoint(new Point(5, 5), window)!.Value, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(box.IsDropDownOpen);
        Assert.Equal(Book(rig).Entries.Select(e => e.Name), Offered(box));

        box.SelectedItem = Book(rig).Entries.Single(e => e.Name == "swiss and europe");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("swiss and europe", Book(rig).PickerText);
        Assert.Equal("swiss and europe", Book(rig).JoinTarget?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public void Leaving_the_box_empty_puts_the_pick_back()
    {
        Rig rig = new();
        Book(rig).Select("AirSherpa");
        var (window, box, input) = Show(rig);

        input.Focus();
        input.SelectAll();
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("", Book(rig).PickerText);

        window.Focus();
        box.Focus();
        input.Focus();
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("AirSherpa", Book(rig).PickerText);
        window.Close();
    }
}
