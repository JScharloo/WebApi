﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.ModelBinding;
using System.Web.Http.ValueProviders;
using System.Web.OData.Extensions;
using System.Web.OData.Properties;
using Microsoft.OData.Core;
using Microsoft.OData.Core.UriParser;
using Microsoft.OData.Edm;

namespace System.Web.OData.Formatter
{
    /// <summary>
    /// Provides a <see cref="IModelBinder"/> for EDM primitive types.
    /// </summary>
    public class ODataModelBinderProvider : ModelBinderProvider
    {
        /// <inheritdoc />
        public override IModelBinder GetBinder(HttpConfiguration configuration, Type modelType)
        {
            if (configuration == null)
            {
                throw Error.ArgumentNull("configuration");
            }

            if (modelType == null)
            {
                throw Error.ArgumentNull("modelType");
            }

            if (EdmLibHelpers.GetEdmPrimitiveTypeOrNull(modelType) != null)
            {
                return new ODataModelBinder();
            }

            if (TypeHelper.IsEnum(modelType))
            {
                return new ODataModelBinder();
            }

            return null;
        }

        internal class ODataModelBinder : IModelBinder
        {
            private static MethodInfo enumTryParseMethod = typeof(Enum).GetMethods()
                        .Single(m => m.Name == "TryParse" && m.GetParameters().Length == 2);

            [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We don't want to fail in model binding.")]
            public bool BindModel(HttpActionContext actionContext, ModelBindingContext bindingContext)
            {
                if (bindingContext == null)
                {
                    throw Error.ArgumentNull("bindingContext");
                }

                if (bindingContext.ModelMetadata == null)
                {
                    throw Error.Argument("bindingContext", SRResources.ModelBinderUtil_ModelMetadataCannotBeNull);
                }

                ValueProviderResult value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
                if (value == null)
                {
                    return false;
                }
                bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);

                try
                {
                    HttpConfiguration config = actionContext.Request.GetConfiguration();
                    TimeZoneInfo timeZoneInfo = config.GetTimeZoneInfo();
                    string valueString = value.RawValue as string;
                    object model = ConvertTo(valueString, bindingContext.ModelType, timeZoneInfo);
                    bindingContext.Model = model;
                    return true;
                }
                catch (ODataException ex)
                {
                    bindingContext.ModelState.AddModelError(bindingContext.ModelName, ex.Message);
                    return false;
                }
                catch (ValidationException ex)
                {
                    bindingContext.ModelState.AddModelError(bindingContext.ModelName, Error.Format(SRResources.ValueIsInvalid, value.RawValue, ex.Message));
                    return false;
                }
                catch (FormatException ex)
                {
                    bindingContext.ModelState.AddModelError(bindingContext.ModelName, Error.Format(SRResources.ValueIsInvalid, value.RawValue, ex.Message));
                    return false;
                }
                catch (Exception e)
                {
                    bindingContext.ModelState.AddModelError(bindingContext.ModelName, e);
                    return false;
                }
            }

            internal static object ConvertTo(string valueString, Type type, TimeZoneInfo timeZoneInfo)
            {
                if (valueString == null)
                {
                    return null;
                }

                // TODO 1668: ODL beta1's ODataUriUtils.ConvertFromUriLiteral does not support converting uri literal
                // to ODataEnumValue, but beta1's ODataUriUtils.ConvertToUriLiteral supports converting ODataEnumValue
                // to uri literal.
                if (TypeHelper.IsEnum(type))
                {
                    string[] values = valueString.Split(new[] { '\'' }, StringSplitOptions.None);
                    if (values.Length == 3 && String.IsNullOrEmpty(values[2]))
                    {
                        // Remove the type name if the enum value is a fully qualified literal.
                        valueString = values[1];
                    }

                    if (type.IsNullable() && String.Equals(valueString, "null", StringComparison.Ordinal))
                    {
                        return null;
                    }

                    Type enumType = TypeHelper.GetUnderlyingTypeOrSelf(type);
                    object[] parameters = new[] { valueString, Enum.ToObject(enumType, 0) };
                    bool isSuccessful = (bool)enumTryParseMethod.MakeGenericMethod(enumType).Invoke(null, parameters);

                    if (!isSuccessful)
                    {
                        throw Error.InvalidOperation(SRResources.ModelBinderUtil_ValueCannotBeEnum, valueString, type.Name);
                    }

                    return parameters[1];
                }

                object value = ODataUriUtils.ConvertFromUriLiteral(valueString, ODataVersion.V4);

                bool isNonStandardEdmPrimitive;
                EdmLibHelpers.IsNonstandardEdmPrimitive(type, out isNonStandardEdmPrimitive);

                if (isNonStandardEdmPrimitive)
                {
                    return EdmPrimitiveHelpers.ConvertPrimitiveValue(value, type, timeZoneInfo);
                }
                else
                {
                    type = Nullable.GetUnderlyingType(type) ?? type;
                    return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
