namespace Abm.Core.Attributes;

  [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
  public sealed class EnumInfoAttribute(string code, string description = "Enum description not defined") : Attribute
  {
    public string Code { get; } = code;

    public string Description { get; } = description;
    
  }

