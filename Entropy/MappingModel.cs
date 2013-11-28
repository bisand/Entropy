using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Entropy
{
    internal class MappingModel
    {
        public Type SourceType { get; set; }
        public Type DestType { get; set; }
        public Dictionary<MemberInfo, MemberInfo> MappingTable { get; set; }
    }
}
