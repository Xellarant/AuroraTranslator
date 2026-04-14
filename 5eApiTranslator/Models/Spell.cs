using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator.Models
{
    class Spell
    {
        public string index { get; set;}
        public string name { get; set; }
        public List<string> desc { get; set; }
        public List<string> higher_level { get; set; }
        public string range { get; set; }
        public List<string> components { get; set; }
        public bool hasVerbal
        {
            get
            {
                return components?.Contains("V") == true;
            }
        }
        public bool hasSomatic
        {
            get
            {
                return components?.Contains("S") == true;
            }
        }
        public bool hasMaterial 
        {
            get
            {
                return components?.Contains("M") == true;
            }
        }
        public string material { get; set; }
        public bool ritual { get; set; }
        public string duration { get; set; }
        public bool concentration { get; set; }
        public string casting_time { get; set; }
        public int level { get; set; }
        public string attack_type { get; set; }
        public DamageComposite damage { get; set; }
        public SpellDC dc { get; set; }
        public BaseApiClass school { get; set; }
        public List<BaseApiClass> classes { get; set; }
        public List<BaseApiClass> subclasses { get; set; }
        public string url { get; set; }        
    }
}
