#nullable enable
using System;
using System.Collections.Generic;

public class Scene
{
    private List<Device> _devices;
    private Scene()
    {
        this._devices = new List<Device>();
    }
    private Scene(string name, IEnumerable<Device>? devices = null, bool isActive = false)
    {
        this.IsActive = isActive;
        this.Name = name;
        this._devices = new List<Device>(devices ?? new List<Device>());
        this.InitializeSubscriptions();
    }
    public bool IsActive { get; private set; }
    public string Name { get; private set; } = default!;
    public IReadOnlyList<Device> Devices
    {
        get => this._devices;
    }
    private DomainResult<Device> CreateDevices(string name, long power, long maxLevel = 100L)
    {
        var deviceResult = Device.Create(name, power, maxLevel);
        if (!deviceResult.IsSuccess)
        {
            return DomainResult<Device>.Failure(deviceResult.ErrorMessage ?? "");
        }
        var device = deviceResult.Value;
        this._devices.Add(device);
        device.RegisterSceneDevicesOnSubscriber(this);
        device.RegisterSceneDevicesOffSubscriber(this);
        return DomainResult<Device>.Success(device);
    }
    internal void AttachDevices(Device child)
    {
        this._devices.Add(child);
        child.RegisterSceneDevicesOnSubscriber(this);
        child.RegisterSceneDevicesOffSubscriber(this);
    }
    public DomainResult Activate()
    {
        var matched1 = false;
        foreach (var target0 in this.Devices)
        {
            matched1 = true;
            var result2 = target0.TurnOn();
            if (!result2.IsSuccess)
            {
                return DomainResult.Failure(result2.ErrorMessage ?? "");
            }
        }
        if (!matched1)
        {
            return DomainResult.Failure("for devices.TurnOn matched zero targets.");
        }
        return DomainResult.Success();
    }
    public DomainResult Deactivate()
    {
        var matched1 = false;
        foreach (var target0 in this.Devices)
        {
            if (!(target0.CurrentStage == DeviceStage.On))
            {
                continue;
            }
            matched1 = true;
            var result2 = target0.TurnOff();
            if (!result2.IsSuccess)
            {
                return DomainResult.Failure(result2.ErrorMessage ?? "");
            }
        }
        if (!matched1)
        {
            return DomainResult.Failure("for devices.TurnOff matched zero targets.");
        }
        return DomainResult.Success();
    }
    public DomainResult DimIdle()
    {
        var matched1 = false;
        foreach (var target0 in this.Devices)
        {
            if (!(target0.CurrentStage == DeviceStage.Off))
            {
                continue;
            }
            matched1 = true;
            var result2 = target0.SetLevel(target0.MaxLevel);
            if (!result2.IsSuccess)
            {
                return DomainResult.Failure(result2.ErrorMessage ?? "");
            }
        }
        if (!matched1)
        {
            return DomainResult.Failure("for devices.SetLevel matched zero targets.");
        }
        return DomainResult.Success();
    }
    internal void WhenAnyDeviceOn()
    {
        var linkedMatched = false;
        foreach (var linkedTarget in this.Devices)
        {
            if (linkedTarget.CurrentStage == DeviceStage.On)
            {
                linkedMatched = true;
            }
        }
        if (!linkedMatched)
        {
            return;
        }
        {
            this.IsActive = true;
        }
    }
    internal void WhenAllDeviceOff(DeviceStage previousStage)
    {
        var linkedMatched = false;
        foreach (var linkedTarget in this.Devices)
        {
            linkedMatched = true;
            if (linkedTarget.CurrentStage != DeviceStage.Off)
            {
                return;
            }
        }
        if (!linkedMatched)
        {
            return;
        }
        if (previousStage == DeviceStage.Off)
        {
            return;
        }
        {
            this.IsActive = false;
        }
    }
    public static DomainResult<Scene> Create(string name, IEnumerable<Device>? devices = null, bool isActive = false)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<Scene>.Failure("'Name' is required.");
        }
        var created = new Scene(name, devices, isActive);
        return DomainResult<Scene>.Success(created);
    }
    private void InitializeSubscriptions()
    {
        foreach (var target in this.Devices)
        {
            target.RegisterSceneDevicesOnSubscriber(this);
        }
        foreach (var target in this.Devices)
        {
            target.RegisterSceneDevicesOffSubscriber(this);
        }
    }
    public DomainResult EnsureUnique(string propertyName, object value) => DomainResult.Success();
    public DomainResult<object> Create(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> Create(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindCreate(name, values);
    }
    public DomainResult<object> CreateIn(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindCreateIn(name, values);
    }
    public DomainResult<object> CreateIn(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindCreateIn(name, values);
    }
    public DomainResult ProbeCreate(string name, object? __job = null)
    {
        var values = new Dictionary<string, object>();
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        return this.BindProbeCreate(name, values);
    }
    public DomainResult ProbeCreate(string name, string p0, object v0, string p1, object v1, string p2, object v2, string p3, object v3, string p4, object v4, string p5, object v5, string p6, object v6, string p7, object v7)
    {
        var values = new Dictionary<string, object>();
        values[p0] = v0;
        values[p1] = v1;
        values[p2] = v2;
        values[p3] = v3;
        values[p4] = v4;
        values[p5] = v5;
        values[p6] = v6;
        values[p7] = v7;
        return this.BindProbeCreate(name, values);
    }
    private DomainResult<object> BindCreate(string typeName, Dictionary<string, object> values)
    {
        if (typeName == "Scene")
        {
            var typed = Scene.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), new List<Device>(), (values.ContainsKey("IsActive") ? (bool?)values["IsActive"] ?? false : false));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        if (typeName == "Device")
        {
            var typed = Device.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("Power") ? (long?)values["Power"] ?? 0L : 0L), (values.ContainsKey("MaxLevel") ? (long?)values["MaxLevel"] ?? 0L : 0L));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        return DomainResult<object>.Failure("Unknown type '" + typeName + "'.");
    }
    private DomainResult<object> BindCreateIn(string relationshipName, Dictionary<string, object> values)
    {
        if (relationshipName == "devices")
        {
            var typed = this.CreateDevices((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("Power") ? (long?)values["Power"] ?? 0L : 0L), (values.ContainsKey("MaxLevel") ? (long?)values["MaxLevel"] ?? 0L : 0L));
            if (!typed.IsSuccess)
            {
                return DomainResult<object>.Failure(typed.ErrorMessage ?? "");
            }
            else
            {
                return DomainResult<object>.Success(typed.Value);
            }
        }
        return DomainResult<object>.Failure("Unknown relationship '" + relationshipName + "'.");
    }
    private DomainResult BindProbeCreate(string typeName, Dictionary<string, object> values)
    {
        if (typeName == "Scene")
        {
            var typed = Scene.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), new List<Device>(), (values.ContainsKey("IsActive") ? (bool?)values["IsActive"] ?? false : false));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        if (typeName == "Device")
        {
            var typed = Device.Create((values.ContainsKey("Name") ? (string?)values["Name"] ?? "" : ""), (values.ContainsKey("Power") ? (long?)values["Power"] ?? 0L : 0L), (values.ContainsKey("MaxLevel") ? (long?)values["MaxLevel"] ?? 0L : 0L));
            if (!typed.IsSuccess)
            {
                return DomainResult.Failure(typed.ErrorMessage ?? "");
            }
            return DomainResult.Success();
        }
        return DomainResult.Failure("Unknown type '" + typeName + "'.");
    }
}