using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

/// <summary>A chat the test writes to: what the others said, and what was sent to it.</summary>
internal sealed class ScriptedChat : IChatSource
{
    public List<ChatMessage> Lines { get; set; } = [new("Ann", "hello", "G-ABCD", 1), new("Bob", "hi", "", 2)];
    public bool Connected { get; set; } = true;
    public bool Allowed { get; set; } = true;
    public bool Unread { get; set; }
    public double Since { get; set; }
    public int Arrived { get; set; }
    public List<string> Sent { get; } = [];
    public int Reads { get; private set; }
    public int MarkedRead { get; private set; }

    public IReadOnlyList<ChatMessage> GetMessages()
    {
        Reads++;
        return [.. Lines];
    }

    public bool IsConnected => Connected;
    public bool CanSend => Connected && Allowed;
    public void Send(string text) => Sent.Add(text);
    public bool HasUnread => Unread;
    public double ReadUpTo => Since;
    public int Arrivals => Arrived;

    public void MarkRead()
    {
        MarkedRead++;
        Since = double.MaxValue;
        Unread = false;
    }
}

public class ChatTests
{
    private static ChatViewModel Open(ScriptedChat chat) => new(chat);

    [Fact]
    public void The_chat_shows_what_the_session_said_oldest_first()
    {
        ChatViewModel tab = Open(new ScriptedChat());

        Assert.Equal(["hello", "hi"], tab.Messages.Select(m => m.Text));
    }

    [Fact]
    public void A_line_says_who_said_it_and_under_which_callsign()
    {
        ChatViewModel tab = Open(new ScriptedChat());

        Assert.Equal("Ann · G-ABCD: ", tab.Messages[0].Label);
        Assert.Equal("Bob: ", tab.Messages[1].Label);
    }

    [Fact]
    public void A_line_of_joinfs_has_no_speaker()
    {
        ChatMessage line = new("", "Command list:", IsLocal: true);

        Assert.Equal("", line.Label);
    }

    [Fact]
    public void A_line_shows_the_time_of_day_it_was_said()
    {
        ChatMessage line = new("Ann", "hello", At: new DateTime(2026, 10, 10, 19, 41, 0));

        Assert.StartsWith(new DateTime(2026, 10, 10, 19, 41, 0).ToString("t"), line.Stamp);
        Assert.Equal("", new ChatMessage("Ann", "hello").Stamp);
    }

    [Fact]
    public void Your_own_line_is_marked_and_does_not_carry_your_name()
    {
        ChatMessage line = new("Me", "on my way", "G-ABCD", IsOwn: true);

        Assert.True(line.IsOwn);
        Assert.Equal("", line.Label);
    }

    [Fact]
    public void New_lines_are_added_at_the_end_without_rebuilding_the_rest()
    {
        ScriptedChat chat = new();
        ChatViewModel tab = Open(chat);
        List<string> changes = [];
        tab.Messages.CollectionChanged += (_, e) => changes.Add(e.Action.ToString());

        chat.Lines.Add(new ChatMessage("Cy", "ahoy", "", 3));
        tab.Refresh();

        Assert.Equal(["hello", "hi", "ahoy"], tab.Messages.Select(m => m.Text));
        Assert.Equal(["Add"], changes);
    }

    [Fact]
    public void Lines_that_expired_are_taken_off_the_top()
    {
        ScriptedChat chat = new();
        ChatViewModel tab = Open(chat);

        chat.Lines.RemoveAt(0);
        tab.Refresh();

        Assert.Equal(["hi"], tab.Messages.Select(m => m.Text));
    }

    [Fact]
    public void Two_lines_that_read_alike_but_came_at_different_times_are_two_lines()
    {
        ScriptedChat chat = new() { Lines = [new("Ann", "ok", "", 1)] };
        ChatViewModel tab = Open(chat);

        chat.Lines.Add(new ChatMessage("Ann", "ok", "", 2));
        tab.Refresh();

        Assert.Equal(2, tab.Messages.Count);
    }

    [Fact]
    public void Sending_needs_text_hands_it_over_trimmed_and_clears_the_line()
    {
        ScriptedChat chat = new();
        ChatViewModel tab = Open(chat);
        Assert.False(tab.SendCommand.CanExecute(null));

        tab.Draft = "  hello there ";
        Assert.True(tab.SendCommand.CanExecute(null));
        tab.SendCommand.Execute(null);

        Assert.Equal(["hello there"], chat.Sent);
        Assert.Equal("", tab.Draft);
    }

    [Fact]
    public void What_was_sent_shows_among_the_lines_at_once()
    {
        FakeChatSource chat = new();
        ChatViewModel tab = Open2(chat);

        tab.Draft = "on my way";
        tab.SendCommand.Execute(null);

        Assert.Equal("on my way", tab.Messages[^1].Text);
    }

    private static ChatViewModel Open2(IChatSource chat) => new(chat);

    [Fact]
    public void A_message_cannot_be_sent_right_after_another()
    {
        ScriptedChat chat = new() { Allowed = false };
        ChatViewModel tab = Open(chat);
        tab.Draft = "again";
        Assert.False(tab.SendCommand.CanExecute(null));

        chat.Allowed = true;
        tab.Refresh(); // the time has passed

        Assert.True(tab.SendCommand.CanExecute(null));
    }

    [Fact]
    public void Without_a_session_the_line_is_off_and_says_what_to_do()
    {
        ScriptedChat chat = new() { Connected = false, Lines = [] };
        ChatViewModel tab = Open(chat);

        Assert.False(tab.IsConnected);
        Assert.Equal("Join a hub to chat", tab.ComposerHint);

        chat.Connected = true;
        tab.Refresh();
        Assert.True(tab.IsConnected);
        Assert.Equal("Type a message, or .help for the commands", tab.ComposerHint);
    }

    // ---- the dot

    private static MainViewModel OpenMain(ScriptedChat chat) =>
        new(FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Chat = chat });

    [Fact]
    public void The_dot_follows_what_arrived_while_the_chat_was_not_on_screen()
    {
        ScriptedChat chat = new() { Unread = false };
        MainViewModel main = OpenMain(chat);
        Assert.False(main.HasNewChat);

        chat.Unread = true;
        main.Poll();

        Assert.True(main.HasNewChat);
    }

    [Fact]
    public void Looking_at_the_chat_clears_the_dot_and_keeps_it_clear_as_lines_come()
    {
        ScriptedChat chat = new() { Unread = true };
        MainViewModel main = OpenMain(chat);
        main.GoTo(TabId.Chat);
        Assert.False(main.HasNewChat);

        chat.Lines.Add(new ChatMessage("Cy", "ahoy", "", 3));
        chat.Unread = true; // it arrived while the chat is on screen
        main.Poll();

        Assert.False(main.HasNewChat);
        Assert.Equal("ahoy", main.Chat.Messages[^1].Text); // and it is shown without the user doing anything
        Assert.False(chat.Unread);
    }

    [Fact]
    public void A_collapsed_window_on_the_chat_tab_is_not_looking_at_it()
    {
        ScriptedChat chat = new();
        MainViewModel main = OpenMain(chat);
        main.GoTo(TabId.Chat);
        main.IsExpanded = false;

        chat.Unread = true;
        main.Poll();

        Assert.True(main.HasNewChat);
    }

    [Fact]
    public void The_chat_is_not_read_while_another_tab_is_shown()
    {
        ScriptedChat chat = new();
        MainViewModel main = OpenMain(chat);
        main.GoTo(TabId.Aircraft);
        int atStart = chat.Reads;

        for (int i = 0; i < 8; i++)
            main.Poll();

        Assert.Equal(atStart, chat.Reads);
    }

    // ---- the separator

    private static ScriptedChat ThreeLines(double readUpTo) => new()
    {
        Lines = [new("Ann", "one", "", 1), new("Bob", "two", "", 2), new("Cy", "three", "", 3)],
        Since = readUpTo,
    };

    private static string[] Marked(MainViewModel main) => [.. main.Chat.Messages.Where(m => m.IsFirstUnread).Select(m => m.Text)];

    [Fact]
    public void Opening_the_chat_marks_the_first_line_that_was_not_seen()
    {
        MainViewModel main = OpenMain(ThreeLines(1.5));

        main.GoTo(TabId.Chat);

        Assert.Equal(["two"], Marked(main));
    }

    [Fact]
    public void Nothing_is_marked_when_every_line_is_unseen_or_none_is()
    {
        MainViewModel all = OpenMain(ThreeLines(0));
        all.GoTo(TabId.Chat);
        Assert.Empty(Marked(all));

        MainViewModel none = OpenMain(ThreeLines(3));
        none.GoTo(TabId.Chat);
        Assert.Empty(Marked(none));
    }

    [Fact]
    public void What_you_said_yourself_is_never_the_first_unseen_line()
    {
        ScriptedChat chat = new() { Lines = [new("Ann", "one", "", 1), new("Me", "mine", "", 2, IsOwn: true), new("Bob", "two", "", 3)], Since = 1 };
        MainViewModel main = OpenMain(chat);

        main.GoTo(TabId.Chat);

        Assert.Equal(["two"], Marked(main));
    }

    [Fact]
    public void The_separator_stays_where_it_was_while_lines_come_in_front_of_you()
    {
        ScriptedChat chat = ThreeLines(1.5);
        MainViewModel main = OpenMain(chat);
        main.GoTo(TabId.Chat);

        chat.Lines.Add(new ChatMessage("Di", "four", "", 4));
        main.Poll();

        Assert.Equal(["two"], Marked(main));
        Assert.Equal("four", main.Chat.Messages[^1].Text);
    }

    [Fact]
    public void Coming_back_after_reading_marks_only_what_came_since()
    {
        ScriptedChat chat = ThreeLines(1.5);
        MainViewModel main = OpenMain(chat);
        main.GoTo(TabId.Chat);

        main.GoTo(TabId.Aircraft);
        for (int i = 0; i < 8; i++)
            main.Poll();
        chat.Lines.Add(new ChatMessage("Di", "four", "", 4));
        chat.Since = 3.5; // it was read up to the last poll with the chat on screen
        main.GoTo(TabId.Chat);

        Assert.Equal(["four"], Marked(main));
    }

    // ---- the chime

    private static MainViewModel OpenMain(ScriptedChat chat, NullPlatform platform) =>
        new(FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }, platform) with { Chat = chat });

    [Fact]
    public void A_message_that_comes_in_chimes_once_whichever_tab_is_shown()
    {
        ScriptedChat chat = new();
        NullPlatform platform = new();
        MainViewModel main = OpenMain(chat, platform);
        main.GoTo(TabId.Aircraft);

        chat.Arrived++;
        main.Poll();
        main.Poll();

        Assert.Equal(1, platform.Chimes);
    }

    [Fact]
    public void Nothing_chimes_for_what_was_there_before_the_window_opened_or_for_a_quiet_chat()
    {
        ScriptedChat chat = new() { Arrived = 5 };
        NullPlatform platform = new();
        MainViewModel main = OpenMain(chat, platform);

        main.Poll();

        Assert.Equal(0, platform.Chimes);
    }

    [Fact]
    public void The_chime_is_a_playable_wav_of_under_a_second()
    {
        byte[] wave = Chime.WaveFile;

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
        Assert.InRange(wave.Length, 10_000, 100_000);
    }
}
