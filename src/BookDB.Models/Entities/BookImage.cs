using System;

namespace BookDB.Models.Entities;

public class BookImage
{
    public int BookImageId { get; set; }
    public int BookId { get; set; }
    public byte[] ImageData { get; set; } = Array.Empty<byte>();
    public string MimeType { get; set; } = "image/jpeg";
    public bool IsPrimary { get; set; } = false;
    public int DisplayOrder { get; set; } = 0;
    public DateTime Added { get; set; }
    public Book? Book { get; set; }
    public int BookImageTypeId { get; set; } = 0;
    public BookImageType? BookImageType { get; set; }
}
