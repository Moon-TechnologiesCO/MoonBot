namespace MoonBot.Core;

public sealed record StateChangedEventArgs(MoonState From, MoonState To);