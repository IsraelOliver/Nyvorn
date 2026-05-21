namespace Nyvorn.Source.World.Decorations
{
    public enum TreePartType
    {
        TrunkStraight,
        // Socket side is the side where the root connects to the trunk.
        TrunkBaseRightRootSocket,
        TrunkBaseLeftRootSocket,
        // Root side is the side where the root is placed relative to the trunk.
        RootLeft,
        RootRight,
        RootBothSocket,
        BranchSocketRight,
        BranchSocketLeft,
        BranchRight,
        BranchLeft,
        TrunkCutSupport,
        TrunkContinuation,
        Canopy,
        TrunkBaseCut,
        TrunkUpperCut,
        TrunkBareBase,
        TrunkBaseRightRootCutSocket
    }
}
