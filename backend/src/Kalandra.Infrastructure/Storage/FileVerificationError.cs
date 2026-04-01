namespace Kalandra.Infrastructure.Storage;

public enum FileVerificationError
{
    PathTraversal,
    WrongFolder,
    MetadataMismatch,
    FileNotFound,
}
