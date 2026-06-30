namespace Nyvorn.Source.World.Persistence
{
    public sealed class RevisionTracker
    {
        private int revision;
        private int persistedRevision;

        public int Revision => revision;
        public bool HasUnsavedChanges => revision != persistedRevision;

        public void MarkChanged()
        {
            revision++;
        }

        public void MarkPersisted()
        {
            persistedRevision = revision;
        }
    }
}
