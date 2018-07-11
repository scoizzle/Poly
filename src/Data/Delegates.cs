namespace Poly.Data {
    public delegate object GetDelegate(object Object);
    public delegate void SetDelegate(object Object, object Value);

    public delegate object GetMemberDelegate(string Name);
    public delegate void SetMemberDelegate(string Name, object Value);

    public delegate bool TryGetMemberDelegate(string Name, out object Value);
    public delegate bool TrySetMemberDelegate(string Name, object Value);

}
