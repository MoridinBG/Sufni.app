using System;
using System.IO;
using System.Threading.Tasks;

namespace Sufni.App.Infrastructure;

/// <summary>A selected file whose readable content remains owned by the platform boundary.</summary>
public sealed record PickedFile(string Name, Func<Task<Stream>> OpenReadAsync);
