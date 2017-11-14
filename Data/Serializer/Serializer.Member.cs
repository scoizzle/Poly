using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Poly.Data {
    public partial class Serializer {
        public struct Member {
        	public string Name;
            public SerializeObjectDelegate Get;
            public DeserializeObjectDelegate Set;
            public Serializer Serializer;
        }

        public static KeyValueCollection<Member> GetMemberList(Type type) {
        	var info = type.GetTypeInfo();
        	var list = new KeyValueCollection<Member>();

            if (type.IsArray)
                return list;

            foreach (var field in info.DeclaredFields)
                if (!field.IsStatic && field.IsPublic && !field.IsLiteral)
                list.Add(
                	field.Name, 
                	new Member {
                        Name = field.Name,
                		Get = field.GetValue,
                		Set = field.SetValue,
                		Serializer = GetCached(field.FieldType)
    				});

            foreach (var prop in info.DeclaredProperties)
                if (prop.CanRead && prop.CanWrite)
                list.Add(
                	prop.Name, 
                	new Member {
                        Name = prop.Name,
                		Get = prop.GetValue,
                		Set = prop.SetValue,
                		Serializer = GetCached(prop.PropertyType)
    				});

            var parent_type = info.BaseType;

            if (parent_type == null)
                return list;

            var parent_info = parent_type.GetTypeInfo();

            foreach (var field in parent_info.DeclaredFields)
                if (!field.IsStatic && field.IsPublic && !field.IsLiteral)
                list.Add(
                    field.Name, 
                    new Member {
                        Name = field.Name,
                        Get = field.GetValue,
                        Set = field.SetValue,
                        Serializer = GetCached(field.FieldType)
                    });

            foreach (var prop in parent_info.DeclaredProperties)
                if (prop.CanRead && prop.CanWrite)
                list.Add(
                    prop.Name, 
                    new Member {
                        Name = prop.Name,
                        Get = prop.GetValue,
                        Set = prop.SetValue,
                        Serializer = GetCached(prop.PropertyType)
                    });

            return list;
        }
    }
}