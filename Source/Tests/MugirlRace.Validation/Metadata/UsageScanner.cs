using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MugirlRace.Validation.Metadata;

/// <summary>
/// 列举模块引用类型或成员的每一处：基类型、接口、特性、字段与签名类型、局部变量、异常处理和 IL 操作数。用于指出被禁引用<em>在哪里</em>。
/// </summary>
internal static class UsageScanner
{
    public static IEnumerable<Usage> Scan(ModuleDefinition module)
    {
        foreach (TypeDefinition type in CecilNames.AllTypes(module))
        {
            foreach (Usage usage in TypeUsages(type, null, type.BaseType))
            {
                yield return usage;
            }

            foreach (InterfaceImplementation implementation in type.Interfaces)
            {
                foreach (Usage usage in TypeUsages(type, null, implementation.InterfaceType))
                {
                    yield return usage;
                }
            }

            foreach (Usage usage in AttributeUsages(type, null, type))
            {
                yield return usage;
            }

            foreach (FieldDefinition field in type.Fields)
            {
                foreach (Usage usage in TypeUsages(type, null, field.FieldType).Concat(AttributeUsages(type, null, field)))
                {
                    yield return usage;
                }
            }

            foreach (PropertyDefinition property in type.Properties)
            {
                foreach (Usage usage in TypeUsages(type, null, property.PropertyType).Concat(AttributeUsages(type, null, property)))
                {
                    yield return usage;
                }
            }

            foreach (EventDefinition @event in type.Events)
            {
                foreach (Usage usage in TypeUsages(type, null, @event.EventType).Concat(AttributeUsages(type, null, @event)))
                {
                    yield return usage;
                }
            }

            foreach (Usage usage in ConstraintUsages(type, null, type.GenericParameters))
            {
                yield return usage;
            }

            foreach (MethodDefinition method in type.Methods)
            {
                foreach (Usage usage in MethodUsages(type, method))
                {
                    yield return usage;
                }
            }
        }
    }

    private static IEnumerable<Usage> MethodUsages(TypeDefinition type, MethodDefinition method)
    {
        foreach (Usage usage in TypeUsages(type, method, method.ReturnType).Concat(AttributeUsages(type, method, method)))
        {
            yield return usage;
        }

        foreach (ParameterDefinition parameter in method.Parameters)
        {
            foreach (Usage usage in TypeUsages(type, method, parameter.ParameterType).Concat(AttributeUsages(type, method, parameter)))
            {
                yield return usage;
            }
        }

        foreach (MethodReference overridden in method.Overrides)
        {
            foreach (Usage usage in MemberUsages(type, method, overridden))
            {
                yield return usage;
            }
        }

        foreach (Usage usage in ConstraintUsages(type, method, method.GenericParameters))
        {
            yield return usage;
        }

        if (!method.HasBody)
        {
            yield break;
        }

        foreach (VariableDefinition variable in method.Body.Variables)
        {
            foreach (Usage usage in TypeUsages(type, method, variable.VariableType))
            {
                yield return usage;
            }
        }

        foreach (ExceptionHandler handler in method.Body.ExceptionHandlers)
        {
            foreach (Usage usage in TypeUsages(type, method, handler.CatchType))
            {
                yield return usage;
            }
        }

        foreach (Instruction instruction in method.Body.Instructions)
        {
            IEnumerable<Usage> usages = instruction.Operand switch
            {
                TypeReference operandType => TypeUsages(type, method, operandType),
                MemberReference member => MemberUsages(type, method, member),
                _ => [],
            };
            foreach (Usage usage in usages)
            {
                yield return usage;
            }
        }
    }

    private static IEnumerable<Usage> MemberUsages(TypeDefinition type, MethodDefinition? method, MemberReference member)
    {
        yield return new Usage(type, method, member);
        foreach (Usage usage in TypeUsages(type, method, member.DeclaringType))
        {
            yield return usage;
        }

        IEnumerable<TypeReference> signature = member switch
        {
            GenericInstanceMethod generic => generic.GenericArguments.Append(generic.ReturnType).Concat(generic.Parameters.Select(p => p.ParameterType)),
            MethodReference called => called.Parameters.Select(p => p.ParameterType).Append(called.ReturnType),
            FieldReference field => [field.FieldType],
            _ => [],
        };
        foreach (TypeReference part in signature)
        {
            foreach (Usage usage in TypeUsages(type, method, part))
            {
                yield return usage;
            }
        }
    }

    private static IEnumerable<Usage> TypeUsages(TypeDefinition type, MethodDefinition? method, TypeReference? referenced)
    {
        foreach (TypeReference component in CecilNames.Components(referenced))
        {
            if (component is not GenericParameter)
            {
                yield return new Usage(type, method, component);
            }
        }
    }

    private static IEnumerable<Usage> ConstraintUsages(TypeDefinition type, MethodDefinition? method, IEnumerable<GenericParameter> parameters)
    {
        foreach (GenericParameter parameter in parameters)
        {
            foreach (GenericParameterConstraint constraint in parameter.Constraints)
            {
                foreach (Usage usage in TypeUsages(type, method, constraint.ConstraintType))
                {
                    yield return usage;
                }
            }
        }
    }

    private static IEnumerable<Usage> AttributeUsages(TypeDefinition type, MethodDefinition? method, ICustomAttributeProvider provider)
    {
        if (!provider.HasCustomAttributes)
        {
            yield break;
        }

        foreach (CustomAttribute attribute in provider.CustomAttributes)
        {
            foreach (Usage usage in MemberUsages(type, method, attribute.Constructor))
            {
                yield return usage;
            }

            // typeof(...) 特性实参（如 [HarmonyPatch(typeof(Target))]）按名称存储，运行时才解析。
            IEnumerable<CustomAttributeArgument> arguments = attribute.ConstructorArguments
                .Concat(attribute.Fields.Select(f => f.Argument)).Concat(attribute.Properties.Select(p => p.Argument));
            foreach (TypeReference argument in arguments.SelectMany(TypeArguments))
            {
                foreach (Usage usage in TypeUsages(type, method, argument))
                {
                    yield return usage;
                }
            }
        }
    }

    private static IEnumerable<TypeReference> TypeArguments(CustomAttributeArgument argument) => argument.Value switch
    {
        TypeReference type => [type],
        CustomAttributeArgument[] array => array.SelectMany(TypeArguments),
        CustomAttributeArgument boxed => TypeArguments(boxed),
        _ => [],
    };
}
