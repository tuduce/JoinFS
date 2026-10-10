namespace JoinFS.Tests;

// The keys of a shortcut are read from the settings the old Shortcuts window wrote. Which keys are down is asked of the system, so only
// the reading of the combination is tested.
public class ShortcutTests
{
    [Theory]
    [InlineData("CTRL+N", true, false, false, 'N')]
    [InlineData("CTRL+SHIFT+R", true, true, false, 'R')]
    [InlineData("ALT+X", false, false, true, 'X')]
    [InlineData("CTRL+SHIFT+ALT+Z", true, true, true, 'Z')]
    [InlineData("Q", false, false, false, 'Q')]
    public void A_combination_is_read_into_modifiers_and_a_letter(string combination, bool control, bool shift, bool alt, char letter)
    {
        Shortcut shortcut = new();

        shortcut.Load(combination);

        Assert.Equal(combination, shortcut.combination);
        Assert.Equal(control, shortcut.control);
        Assert.Equal(shift, shortcut.shift);
        Assert.Equal(alt, shortcut.alt);
        Assert.Equal(letter, (char)shortcut.letter);
    }

    [Fact]
    public void A_letter_that_is_not_one_leaves_the_default_letter_a()
    {
        Shortcut shortcut = new();

        shortcut.Load("CTRL+5");

        Assert.True(shortcut.control);
        Assert.Equal('A', (char)shortcut.letter);
    }

    [Fact]
    public void Loading_again_forgets_the_last_combination()
    {
        Shortcut shortcut = new();
        shortcut.Load("CTRL+SHIFT+R");

        shortcut.Load("ALT+F");

        Assert.False(shortcut.control);
        Assert.False(shortcut.shift);
        Assert.True(shortcut.alt);
        Assert.Equal('F', (char)shortcut.letter);
    }

    [Theory]
    [InlineData("CTRL+N", true)]
    [InlineData("A", true)]
    [InlineData("CTRL+ALT+SHIFT+Z", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("CTRL+", false)]
    [InlineData("CTRL+5", false)]
    [InlineData("CTRL+AB", false)]
    [InlineData("WIN+A", false)]
    [InlineData("ctrl+n", false)] // the settings are upper case
    public void A_valid_shortcut_is_modifiers_then_one_letter(string combination, bool valid)
    {
        Assert.Equal(valid, Shortcut.IsValid(combination));
    }

    [Fact]
    public void The_combination_text_lists_the_modifiers_in_the_order_the_old_window_wrote_them()
    {
        Assert.Equal("CTRL+SHIFT+ALT+K", Shortcut.Compose(true, true, true, 'K'));
        Assert.Equal("K", Shortcut.Compose(false, false, false, 'K'));
        Assert.True(Shortcut.IsValid(Shortcut.Compose(true, false, true, 'K')));
    }
}
