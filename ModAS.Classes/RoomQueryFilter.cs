namespace ModAS.Classes;

public class RoomQueryFilter {
    public string? RoomIdContains { get; set; }
    public string? NameContains { get; set; }
    public string? CanonicalAliasContains { get; set; }
    public string? VersionContains { get; set; }
    public string? CreatorContains { get; set; }
    public string? EncryptionContains { get; set; }
    public string? JoinRulesContains { get; set; }
    public string? GuestAccessContains { get; set; }
    public string? HistoryVisibilityContains { get; set; }
    public string? AvatarUrlContains { get; set; }
    public string? RoomTopicContains { get; set; }

    public bool? IsFederatable { get; set; } = true;
    public bool? IsPublic { get; set; } = true;

    public uint? JoinedMembersMin { get; set; }
    public uint? JoinedMembersMax { get; set; }
    public uint? JoinedLocalMembersMin { get; set; }
    public uint? JoinedLocalMembersMax { get; set; }
    public uint? StateEventsMin { get; set; }
    public uint? StateEventsMax { get; set; }
    
}