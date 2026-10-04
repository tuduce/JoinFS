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

    public void MarkRead()
    {
        MarkedRead++;
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
        Assert.Equal("Type a message", tab.ComposerHint);
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
}
