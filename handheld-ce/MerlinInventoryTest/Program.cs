using System;
using System.Windows.Forms;

namespace MerlinHandheld
{
    static class Program
    {
        [MTAThread]
        static void Main()
        {
            Application.Run(new MainForm());
        }
    }
}
