using Verse;

namespace MooGirl
{
    public enum RopeLinkKind
    {
        Pawn,
        Spot
    }

    public sealed class RopeLink
    {
        public readonly Pawn Roper;
        public readonly Pawn Ropee;
        public readonly LocalTargetInfo Target;
        public readonly RopeLinkKind Kind;

        public RopeLink(Pawn roper, Pawn ropee, LocalTargetInfo target, RopeLinkKind kind)
        {
            Roper = roper;
            Ropee = ropee;
            Target = target;
            Kind = kind;
        }
    }
}
