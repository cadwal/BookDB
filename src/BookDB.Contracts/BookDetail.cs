using System.Collections.Generic;
using ProtoBuf;

namespace BookDB.Contracts;

/// <summary>
/// One book as the phone reads it: everything already resolved to text, because the phone holds none of the
/// desktop's lookup tables. A book deleted between listing it and opening it comes back with
/// <see cref="Found"/> false rather than as an error.
/// </summary>
[ProtoContract]
public sealed class BookDetail
{
    [ProtoMember(1)] public bool Found { get; set; }
    [ProtoMember(2)] public int BookId { get; set; }
    [ProtoMember(3)] public string Title { get; set; } = "";
    [ProtoMember(4)] public string? Subtitle { get; set; }
    [ProtoMember(5)] public string? Authors { get; set; }
    [ProtoMember(6)] public string? Series { get; set; }
    [ProtoMember(7)] public string? Publisher { get; set; }

    /// <summary>Free text on the desktop — a year, a full date, sometimes prose — so it travels as it is stored.</summary>
    [ProtoMember(8)] public string? PubDate { get; set; }

    [ProtoMember(9)] public string? Format { get; set; }
    [ProtoMember(10)] public string? Language { get; set; }

    /// <summary>
    /// The resource key behind a seeded lookup row, so the companion can say the word in *its* language rather
    /// than in whatever language the library was seeded in. Null for a row the user typed themselves, which
    /// has no translation and travels as <see cref="Format"/>/<see cref="Language"/> alone.
    /// </summary>
    [ProtoMember(16)] public string? FormatKey { get; set; }

    [ProtoMember(17)] public string? LanguageKey { get; set; }
    [ProtoMember(11)] public int? Pages { get; set; }
    [ProtoMember(12)] public string? Isbn { get; set; }
    [ProtoMember(13)] public string? Collection { get; set; }
    [ProtoMember(14)] public string? Comments { get; set; }

    /// <summary>Which images the book actually has, so the detail view asks only for those.</summary>
    [ProtoMember(15)] public List<ScanImageType> Images { get; set; } = [];
}
