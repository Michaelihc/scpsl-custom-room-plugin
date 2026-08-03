using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WarmupRangeVerifier;

internal static class ReflectionAccess
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static object? NullableField(object instance, string name)
    {
        if (instance == null)
        {
            throw new InvalidOperationException($"Cannot read field '{name}' from null.");
        }

        FieldInfo? field = FindField(instance.GetType(), name);
        if (field == null)
        {
            throw new MissingFieldException(instance.GetType().FullName, name);
        }

        return field.GetValue(instance);
    }

    public static object Field(object instance, string name)
    {
        return NullableField(instance, name)
            ?? throw new InvalidOperationException($"Field {instance.GetType().FullName}.{name} is null.");
    }

    public static T Field<T>(object instance, string name)
    {
        object value = Field(instance, name);
        if (value is T typed)
        {
            return typed;
        }

        throw new InvalidCastException($"Field {instance.GetType().FullName}.{name} is {value.GetType().FullName}, not {typeof(T).FullName}.");
    }

    public static void SetField(object instance, string name, object value)
    {
        FieldInfo? field = FindField(instance.GetType(), name);
        if (field == null)
        {
            throw new MissingFieldException(instance.GetType().FullName, name);
        }

        field.SetValue(instance, value);
    }

    public static object? Property(object instance, string name)
    {
        if (instance == null)
        {
            throw new InvalidOperationException($"Cannot read property '{name}' from null.");
        }

        PropertyInfo? property = FindProperty(instance.GetType(), name);
        if (property == null)
        {
            throw new MissingMemberException(instance.GetType().FullName, name);
        }

        return property.GetValue(instance, null);
    }

    public static T Property<T>(object instance, string name)
    {
        object? value = Property(instance, name);
        if (value is T typed)
        {
            return typed;
        }

        throw new InvalidCastException($"Property {instance.GetType().FullName}.{name} is not {typeof(T).FullName}.");
    }

    public static object? Invoke(object instance, string name, params object?[] args)
    {
        if (instance == null)
        {
            throw new InvalidOperationException($"Cannot invoke '{name}' on null.");
        }

        MethodInfo[] candidates = instance.GetType().GetMethods(InstanceFlags)
            .Where(method => method.Name == name && method.GetParameters().Length == args.Length)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new MissingMethodException(instance.GetType().FullName, name);
        }

        MethodInfo? compatible = candidates.FirstOrDefault(method => ParametersMatch(method.GetParameters(), args));
        return (compatible ?? candidates[0]).Invoke(instance, args);
    }

    public static object? InvokeStatic(Type type, string name, params object?[] args)
    {
        MethodInfo[] candidates = type.GetMethods(StaticFlags)
            .Where(method => method.Name == name && method.GetParameters().Length == args.Length)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new MissingMethodException(type.FullName, name);
        }

        MethodInfo? compatible = candidates.FirstOrDefault(method => ParametersMatch(method.GetParameters(), args));
        return (compatible ?? candidates[0]).Invoke(null, args);
    }

    public static int Count(object enumerable)
    {
        if (enumerable is ICollection collection)
        {
            return collection.Count;
        }

        int count = 0;
        foreach (object? _ in (IEnumerable)enumerable)
        {
            count++;
        }

        return count;
    }

    public static List<object> Items(object enumerable)
    {
        List<object> result = new List<object>();
        foreach (object? item in (IEnumerable)enumerable)
        {
            if (item != null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, InstanceFlags);
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(name, InstanceFlags);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    private static bool ParametersMatch(ParameterInfo[] parameters, object?[] args)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (args[i] == null)
            {
                if (parameters[i].ParameterType.IsValueType && Nullable.GetUnderlyingType(parameters[i].ParameterType) == null && !parameters[i].IsOut)
                {
                    return false;
                }

                continue;
            }

            Type expected = parameters[i].ParameterType.IsByRef
                ? parameters[i].ParameterType.GetElementType()!
                : parameters[i].ParameterType;
            if (!expected.IsInstanceOfType(args[i]) && !CanConvertNumeric(args[i]!.GetType(), expected))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanConvertNumeric(Type actual, Type expected)
    {
        return actual.IsPrimitive && expected.IsPrimitive && actual != typeof(bool) && expected != typeof(bool);
    }
}
