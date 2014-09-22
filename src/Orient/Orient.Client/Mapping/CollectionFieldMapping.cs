﻿using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace Orient.Client.Mapping
{
    internal class CollectionNamedFieldMapping : NamedFieldMapping
    {
        private readonly TypeMapperBase _mapper;
        private readonly Type _targetElementType;
        private readonly bool _needsMapping;

        public CollectionNamedFieldMapping(PropertyInfo propertyInfo, string fieldPath)
            : base(propertyInfo, fieldPath)
        {
            _targetElementType = GetTargetElementType();
            _needsMapping = !NeedsNoConversion(_targetElementType);
            if (_needsMapping)
                _mapper = TypeMapperBase.GetInstanceFor(_targetElementType);
        }

        protected override void MapToNamedField(ODocument document, object typedObject)
        {
            object sourcePropertyValue = document.GetField<object>(_fieldPath);

            IList collection = sourcePropertyValue as IList;
            if (collection == null) // if we only have one item currently stored (but scope for more) we create a temporary list and put our single item in it.
            {
                collection = new ArrayList();
                if (sourcePropertyValue != null)
                    collection.Add(sourcePropertyValue);
            }

            if (collection.Count > 0)
            {
                // create instance of property type
                IList collectionInstance = (IList) Activator.CreateInstance(_propertyInfo.PropertyType,collection.Count);

                for (int i = 0; i < collection.Count; i++ )
                {
                    var t = collection[i];
                    object oMapped = t;
                    if (_needsMapping)
                    {
                        object element = Activator.CreateInstance(_targetElementType);
                        _mapper.ToObject((ODocument)t, element);
                        oMapped = element;
                    }
                    if (collectionInstance.IsFixedSize)
                    {
                        collectionInstance[i] = oMapped;
                    }
                    else
                    {
                        collectionInstance.Add(oMapped);
                    }
                }

                _propertyInfo.SetValue(typedObject, collectionInstance, null);
            }
            else
            {
                _propertyInfo.SetValue(typedObject, null, null);
            }
        }

        private Type GetTargetElementType()
        {
            if (_propertyInfo.PropertyType.IsArray)
                return _propertyInfo.PropertyType.GetElementType();
            if (_propertyInfo.PropertyType.IsGenericType)
                return _propertyInfo.PropertyType.GetGenericArguments().First();

            throw new NotImplementedException();

        }

        private static bool NeedsNoConversion(Type elementType)
        {
            return elementType.IsPrimitive ||
                   (elementType == typeof (string)) ||
                   (elementType == typeof (DateTime)) ||
                   (elementType == typeof (decimal)) ||
                   (elementType == typeof (ORID));
        }
    }
}