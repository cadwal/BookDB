namespace BookDB.Contracts;

/// <summary>
/// Wire image kinds. The numbers deliberately match the desktop's image-type ids so the mapping stays an
/// identity; 1 is skipped because the desktop reserves it for generated thumbnails, which a phone never
/// uploads.
/// </summary>
public enum ScanImageType
{
    FrontCover = 0,
    BackCover = 2,
    Spine = 3,
    DustJacket = 4,
}
