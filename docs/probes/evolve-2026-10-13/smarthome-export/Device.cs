#nullable enable
using System;
using System.Collections.Generic;

public enum DeviceStage
{
    Off = 0,
    On = 1
}

public class Device
{
    private List<Scene>? _sceneDevicesOnSubscribers;
    private List<Scene>? _sceneDevicesOffSubscribers;
    private int _notifyDepth;
    private Device()
    { }
    private Device(string name, long power, long maxLevel = 100L)
    {
        this.MaxLevel = maxLevel;
        this.Name = name;
        this.Power = power;
        this.CurrentStage = DeviceStage.Off;
        this.Draw = 0L;
    }
    public long Draw { get; private set; }
    public long MaxLevel { get; private set; }
    public string Name { get; private set; } = default!;
    public long Power { get; private set; }
    public DeviceStage CurrentStage { get; private set; }
    public DomainResult TurnOn()
    {
        if (this.CurrentStage != DeviceStage.Off)
        {
            return DomainResult.Failure("'TurnOn' requires stage 'Off' on entity 'Device'.");
        }
        this.Draw = this.Power;
        var previousStage0 = this.CurrentStage;
        this.CurrentStage = DeviceStage.On;
        {
            this.Draw = this.Power;
            this.NotifyOnSubscribers(previousStage0);
        }
        return DomainResult.Success();
    }
    public DomainResult SetLevel(long level)
    {
        if (this.CurrentStage != DeviceStage.Off)
        {
            return DomainResult.Failure("'SetLevel' requires stage 'Off' on entity 'Device'.");
        }
        var assignValue0 = level;
        if (assignValue0 < 0L)
        {
            return DomainResult.Failure("'MaxLevel' must be >= 0.");
        }
        if (assignValue0 > 100L)
        {
            return DomainResult.Failure("'MaxLevel' must be <= 100.");
        }
        this.MaxLevel = assignValue0;
        return DomainResult.Success();
    }
    public DomainResult TurnOff()
    {
        if (this.CurrentStage != DeviceStage.On)
        {
            return DomainResult.Failure("'TurnOff' requires stage 'On' on entity 'Device'.");
        }
        var previousStage0 = this.CurrentStage;
        this.CurrentStage = DeviceStage.Off;
        {
            this.Draw = 0L;
            this.NotifyOffSubscribers(previousStage0);
        }
        return DomainResult.Success();
    }
    internal void RegisterSceneDevicesOnSubscriber(Scene subscriber)
    {
        if (this._sceneDevicesOnSubscribers == null)
        {
            this._sceneDevicesOnSubscribers = new List<Scene>();
        }
        this._sceneDevicesOnSubscribers.Add(subscriber);
    }
    internal void RegisterSceneDevicesOffSubscriber(Scene subscriber)
    {
        if (this._sceneDevicesOffSubscribers == null)
        {
            this._sceneDevicesOffSubscribers = new List<Scene>();
        }
        this._sceneDevicesOffSubscribers.Add(subscriber);
    }
    internal void NotifyOnSubscribers(DeviceStage previousStage)
    {
        this.EnterNotifyCascade("On");
        try
        {
            if (this._sceneDevicesOnSubscribers != null)
            {
                foreach (var sub in this._sceneDevicesOnSubscribers)
                {
                    sub.WhenAnyDeviceOn();
                }
            }
        }
        finally
        {
            this.ExitNotifyCascade();
        }
    }
    internal void NotifyOffSubscribers(DeviceStage previousStage)
    {
        this.EnterNotifyCascade("Off");
        try
        {
            if (this._sceneDevicesOffSubscribers != null)
            {
                foreach (var sub in this._sceneDevicesOffSubscribers)
                {
                    sub.WhenAllDeviceOff(previousStage);
                }
            }
        }
        finally
        {
            this.ExitNotifyCascade();
        }
    }
    public static DomainResult<Device> Create(string name, long power, long maxLevel = 100L)
    {
        if (maxLevel < 0L)
        {
            return DomainResult<Device>.Failure("'MaxLevel' must be >= 0.");
        }
        if (maxLevel > 100L)
        {
            return DomainResult<Device>.Failure("'MaxLevel' must be <= 100.");
        }
        if (string.IsNullOrEmpty(name))
        {
            return DomainResult<Device>.Failure("'Name' is required.");
        }
        if (power < 0L)
        {
            return DomainResult<Device>.Failure("'Power' must be >= 0.");
        }
        if (power > 3000L)
        {
            return DomainResult<Device>.Failure("'Power' must be <= 3000.");
        }
        var created = new Device(name, power, maxLevel);
        return DomainResult<Device>.Success(created);
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
    private DomainResult<object> BindCreateIn(string relationshipName, Dictionary<string, object> values) => DomainResult<object>.Failure("Unknown relationship '" + relationshipName + "'.");
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
    private void EnterNotifyCascade(string stageName)
    {
        if (this._notifyDepth >= 16)
        {
            throw new System.InvalidOperationException(string.Concat("Subscription cascade on entity 'Device' exceeded max depth (16) while notifying '", stageName, "'."));
        }
        this._notifyDepth = this._notifyDepth + 1;
    }
    private void ExitNotifyCascade()
    {
        this._notifyDepth = this._notifyDepth - 1;
    }
    private void OnEntryOff()
    {
        this.Draw = 0L;
    }
    private void OnEntryOn()
    {
        this.Draw = this.Power;
    }
}