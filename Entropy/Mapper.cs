using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Entropy
{
    public class Mapper
    {
        private static readonly Dictionary<string, MappingModel> MappingModels;
        private static readonly HashSet<InterfaceModel> InterfaceModels;
        private static readonly HashSet<SourceDestModel> SourceDestModels;

        static Mapper()
        {
            MappingModels = new Dictionary<string, MappingModel>();
            InterfaceModels = new HashSet<InterfaceModel>();
            SourceDestModels = new HashSet<SourceDestModel>();
        }

        public static void CreateMap<TOwner, TInterface, TConcrete>()
        {
            var interfaceModel = new InterfaceModel();
            interfaceModel.OwnerType = typeof(TOwner);
            interfaceModel.InterfaceType = typeof(TInterface);
            interfaceModel.ConcreteType = typeof(TConcrete);
            InterfaceModels.Add(interfaceModel);
        }

        public static void CreateMap<TSource, TDest>()
        {
            var sourceDestModel = new SourceDestModel();
            sourceDestModel.SourceType = typeof(TSource);
            sourceDestModel.DestType = typeof(TDest);
            SourceDestModels.Add(sourceDestModel);
        }

        public static void CreateDestOwnerMap<TSource, TDest, TDestOwner>()
        {
            var sourceDestModel = new SourceDestModel();
            sourceDestModel.DestOwnerType = typeof(TDestOwner);
            sourceDestModel.SourceType = typeof(TSource);
            sourceDestModel.DestType = typeof(TDest);
            SourceDestModels.Add(sourceDestModel);
        }

        public static void Map<TSource, TDest>(TSource source, TDest dest)
        {
            var sourceType = typeof(TSource);
            var destType = typeof(TDest);
            Map(source, dest, sourceType, destType);
        }

        public static void Map(object source, object dest)
        {
            var sourceType = source.GetType();
            var destType = dest.GetType();
            Map(source, dest, sourceType, destType);
        }

        private static void Map<TSource, TDest>(TSource source, TDest dest, Type sourceType, Type destType)
        {
            var mappingName = string.Format("{0}To{1}", sourceType.Name, destType.Name);

            MappingModel mappingModel;
            if (!MappingModels.TryGetValue(mappingName, out mappingModel))
            {
                mappingModel = new MappingModel();
                mappingModel.SourceType = sourceType;
                mappingModel.DestType = destType;
                mappingModel.MappingTable = FillMemberInfo(sourceType, destType);
                MappingModels.Add(mappingName, mappingModel);
            }

            CopyValues(source, dest, mappingModel);
        }

        private static void CopyValues(object source, object dest, MappingModel mappingModel)
        {
            foreach (var info in mappingModel.MappingTable)
            {
                var sourceInfo = info.Key;
                var destInfo = info.Value;
                CopyValue(sourceInfo, source, destInfo, dest);
            }
        }

        private static void CopyValue(MemberInfo sourceInfo, object source, MemberInfo destInfo, object dest)
        {
            object sourceValue = null;
            if (sourceInfo is FieldInfo)
                sourceValue = (sourceInfo as FieldInfo).GetValue(source);
            else if (sourceInfo is PropertyInfo)
                sourceValue = (sourceInfo as PropertyInfo).GetValue(source, null);

            if (destInfo is FieldInfo)
            {
                CopyFieldValue(sourceInfo as FieldInfo, sourceValue, destInfo as FieldInfo, dest);
            }
            else if (destInfo is PropertyInfo)
            {
                CopyPropertyValue(sourceInfo as PropertyInfo, sourceValue, destInfo as PropertyInfo, dest);
            }
        }

        private static void CopyPropertyValue(PropertyInfo sourceProperty, object sourcePropertyValue, PropertyInfo destProperty, object destObject)
        {
            if (sourceProperty == null || destProperty == null)
                return;

            var processCollections = ProcessCollections(sourceProperty, sourcePropertyValue, destProperty, destObject);
            var type = GetConcreteType(destProperty);
            if (type != null)
            {
                var instance = Activator.CreateInstance(type);
                Map(sourcePropertyValue, instance, sourceProperty.PropertyType, type);
                destProperty.SetValue(destObject, instance, null);
            }
            else
            {
                destProperty.SetValue(destObject, sourcePropertyValue, null);
            }
        }

        private static void CopyFieldValue(FieldInfo sourceField, object sourceFieldValue, FieldInfo destField, object destObject)
        {
            if (sourceField == null || destField == null)
                return;

            var type = GetConcreteType(destField);
            if (type != null)
            {
                var instance = Activator.CreateInstance(type);
                Map(sourceFieldValue, instance, sourceField.FieldType, type);
                destField.SetValue(destObject, instance);
            }
            else
            {
                destField.SetValue(destObject, sourceFieldValue);
            }
        }

        private static bool ProcessCollections(PropertyInfo sourceProperty, object sourcePropertyValue, PropertyInfo destProperty, object destObject)
        {
            if (IsDictionary(sourcePropertyValue))
            {
                var dictionary = sourcePropertyValue as IDictionary;
                if (dictionary == null)
                    return false;

                foreach (var obj in dictionary.Values)
                {
                }
                return true;
            }

            if (IsCollection(sourcePropertyValue))
            {
                var coll = sourcePropertyValue as ICollection;
                var sourceType = coll.GetType();
                var sourceElementType = sourceType.GetElementType() ?? sourceType.GetGenericTypeDefinition();
                //var destElementType = destProperty.PropertyType.GetElementType() ?? destProperty.PropertyType.GenericTypeArguments.FirstOrDefault();

                var ownerType = destObject.GetType();

                //var model = InterfaceModels.FirstOrDefault(x => destElementType != null && (x.OwnerType.FullName == ownerType.FullName && x.InterfaceType.FullName == destElementType.FullName));
                var model = SourceDestModels.FirstOrDefault(x => sourceElementType != null && x.SourceType.FullName == sourceElementType.FullName);
                if (model == null)
                    return false;

                var array = Array.CreateInstance(model.DestType, coll.Count);
                var i = 0;
                foreach (var obj in coll)
                {
                    var instance = Activator.CreateInstance(model.DestType);
                    Map(obj, instance);
                    array.SetValue(instance, i++);
                }

                destProperty.SetValue(destObject, array, null);
                return true;
            }
            return false;
        }

        public static bool IsList(object obj)
        {
            return obj is IList;
        }

        public static bool IsCollection(object obj)
        {
            return obj is ICollection;
        }

        public static bool IsDictionary(object obj)
        {
            return obj is IDictionary;
        }

        private static Dictionary<MemberInfo, MemberInfo> FillMemberInfo(Type sourceType, Type destType)
        {
            var result = new Dictionary<MemberInfo, MemberInfo>();
            var sourceFields = sourceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static);
            var destFields = destType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static);
            foreach (var member in destFields)
                AddMember(member, sourceFields, result);

            var sourceProperties = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static);
            var destProperties = destType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static);
            foreach (var member in destProperties)
                AddMember(member, sourceProperties, result);

            return result;
        }

        private static void AddMember(MemberInfo member, IEnumerable<MemberInfo> sourceFields, IDictionary<MemberInfo, MemberInfo> result)
        {
            var destMember = member;
            var sourceMember = sourceFields.FirstOrDefault(x => x.Name == destMember.Name);
            if (sourceMember != null)
            {
                result.Add(sourceMember, destMember);
            }
        }

        private static Type GetConcreteType(MemberInfo destMember)
        {
            var ownerType = destMember.DeclaringType;
            InterfaceModel model = null;
            if (destMember.MemberType == MemberTypes.Property)
            {
                var prop = (destMember as PropertyInfo);
                if (prop != null)
                    model = InterfaceModels.FirstOrDefault(x => ownerType != null && x.OwnerType.FullName == ownerType.FullName && x.InterfaceType.Name == prop.PropertyType.Name);
            }

            if (destMember.MemberType == MemberTypes.Field)
            {
                var fld = (destMember as FieldInfo);
                if (fld != null)
                    model = InterfaceModels.FirstOrDefault(x => ownerType != null && x.OwnerType.FullName == ownerType.FullName && x.InterfaceType.Name == fld.FieldType.Name);
            }
            if (model != null)
            {
                return model.ConcreteType;
            }
            return null;
        }
    }

    internal class SourceDestModel
    {
        public Type DestOwnerType { get; set; }
        public Type SourceType { get; set; }
        public Type DestType { get; set; }
    }

    internal class InterfaceModel
    {
        public Type OwnerType { get; set; }
        public Type InterfaceType { get; set; }
        public Type ConcreteType { get; set; }
    }
}