using System;
using System.Reflection;
using System.Windows.Forms;

namespace STUN
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new winFormMain());
        }
        
        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs e)
        {
            string _resName = "STUN.Source" + new AssemblyName(e.Name).Name + ".dll";
            using (var _stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(_resName))
            {
                byte[] _data = new byte[_stream.Length];
                _stream.Read(_data, 0, _data.Length);
                return Assembly.Load(_data);
            }
        }
    }
}