namespace Poly.Interpretation.VirtualMachine;

/// <summary>A single µop-level transformation pass.  The pass receives the
/// current µop array and returns a (possibly new) array with the
/// transformation applied.</summary>
public interface IUopPass {
    MicroOp[] Apply(MicroOp[] uops);
}