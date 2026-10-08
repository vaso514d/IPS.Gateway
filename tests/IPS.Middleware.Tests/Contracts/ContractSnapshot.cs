using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace IPS.Middleware.Tests.Contracts;

internal static class ContractSnapshot
{
    public static string Create(Assembly assembly)
    {
        var nullability = new NullabilityInfoContext();
        var types = assembly.GetExportedTypes().OrderBy(TypeName, StringComparer.Ordinal).Select(type => new
        {
            name = TypeName(type),
            kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class",
            isAbstract = type.IsAbstract,
            isSealed = type.IsSealed,
            baseType = type.BaseType is { } parent ? TypeName(parent) : null,
            interfaces = type.GetInterfaces().Select(TypeName).Order(StringComparer.Ordinal).ToArray(),
            attributes = Attributes(type.GetCustomAttributesData()),
            members = Members(type, nullability).Order(StringComparer.Ordinal).ToArray()
        });
        return JsonSerializer.Serialize(types, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n");
    }

    private static IEnumerable<string> Members(Type type, NullabilityInfoContext nullability)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        {
            var value = field.IsLiteral ? " = " + JsonSerializer.Serialize(field.GetRawConstantValue()) : "";
            yield return $"field {TypeName(field.FieldType)} {field.Name}{value} {AttributesText(field.GetCustomAttributesData())}";
        }

        foreach (var property in type.GetProperties(flags))
        {
            var accessors = property.GetAccessors().Select(accessor => accessor.Name + ":" + Modifiers(accessor));
            var index = string.Join(", ", property.GetIndexParameters().Select(parameter => Parameter(parameter, nullability)));
            yield return $"property {TypeName(property.PropertyType)} {property.Name}[{index}] nullable={Nullability(nullability.Create(property))} ({string.Join(", ", accessors)}) {AttributesText(property.GetCustomAttributesData())}";
        }

        foreach (var constructor in type.GetConstructors(flags))
        {
            yield return $"constructor ({string.Join(", ", constructor.GetParameters().Select(parameter => Parameter(parameter, nullability)))}) {AttributesText(constructor.GetCustomAttributesData())}";
        }

        foreach (var method in type.GetMethods(flags))
        {
            yield return $"method {Modifiers(method)} {TypeName(method.ReturnType)} {method.Name} ({string.Join(", ", method.GetParameters().Select(parameter => Parameter(parameter, nullability)))}) returnNullable={Nullability(nullability.Create(method.ReturnParameter))} {AttributesText(method.GetCustomAttributesData())}";
        }

        foreach (var eventInfo in type.GetEvents(flags))
        {
            yield return $"event {TypeName(eventInfo.EventHandlerType!)} {eventInfo.Name} {AttributesText(eventInfo.GetCustomAttributesData())}";
        }
    }

    private static string Parameter(ParameterInfo parameter, NullabilityInfoContext nullability)
    {
        var defaultValue = parameter.HasDefaultValue
            ? " default=" + Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture)
            : "";
        return $"{TypeName(parameter.ParameterType)} {parameter.Name} nullable={Nullability(nullability.Create(parameter))}{defaultValue} {AttributesText(parameter.GetCustomAttributesData())}";
    }

    private static string Modifiers(MethodBase method) =>
        $"static={method.IsStatic},abstract={method.IsAbstract},virtual={method.IsVirtual},final={method.IsFinal}";

    private static string Nullability(NullabilityInfo info) =>
        $"{info.ReadState}/{info.WriteState}" +
        (info.ElementType is { } element ? "[" + Nullability(element) + "]" : "") +
        (info.GenericTypeArguments.Length > 0 ? "<" + string.Join(",", info.GenericTypeArguments.Select(Nullability)) + ">" : "");

    private static string TypeName(Type type)
    {
        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsByRef)
        {
            return TypeName(type.GetElementType()!) + "&";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var name = type.GetGenericTypeDefinition().FullName!;
        return name[..name.IndexOf('`')] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
    }

    private static string[] Attributes(IList<CustomAttributeData> attributes) =>
        attributes.Where(attribute => attribute.AttributeType.FullName is not
                ("System.Runtime.CompilerServices.CompilerGeneratedAttribute" or
                 "System.Runtime.CompilerServices.NullableAttribute" or
                 "System.Runtime.CompilerServices.NullableContextAttribute"))
            .Select(attribute =>
                TypeName(attribute.AttributeType) + "(" +
                string.Join(",", attribute.ConstructorArguments.Select(AttributeValue)) + ")" +
                string.Join(",", attribute.NamedArguments.OrderBy(argument => argument.MemberName, StringComparer.Ordinal)
                    .Select(argument => argument.MemberName + "=" + AttributeValue(argument.TypedValue))))
            .Order(StringComparer.Ordinal).ToArray();

    private static string AttributeValue(CustomAttributeTypedArgument argument) => argument.Value switch
    {
        Type type => TypeName(type),
        IReadOnlyCollection<CustomAttributeTypedArgument> values => "[" + string.Join(",", values.Select(AttributeValue)) + "]",
        _ => JsonSerializer.Serialize(argument.Value)
    };

    private static string AttributesText(IList<CustomAttributeData> attributes) => string.Join(" ", Attributes(attributes));
}
