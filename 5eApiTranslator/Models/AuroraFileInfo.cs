using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator.Models
{
    internal class AuroraFileInfo
    {
        public string RelativePath { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Author Author { get; set; }
        public FileVersion FileVersion { get; set; }
    }
}
