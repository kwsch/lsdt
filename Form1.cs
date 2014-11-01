using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using Ionic.Zlib;

namespace lsdt
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        bool isWorkerThreadAlive = false;

        private void button4_Click(object sender, EventArgs e)//out
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            DialogResult dr = fbd.ShowDialog();
            if (dr == DialogResult.OK)
            {
                textBox3.Text = fbd.SelectedPath;
            }
        }

        private void button2_Click(object sender, EventArgs e)//dt
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Select a dt file";
            ofd.InitialDirectory = Application.StartupPath;
            DialogResult dr = ofd.ShowDialog();
            if (dr == DialogResult.OK)
            {
                textBox2.Text = ofd.FileName;
            }
        }

        private void button1_Click(object sender, EventArgs e) //ls
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Select an ls file";
            ofd.InitialDirectory = Application.StartupPath;
            DialogResult dr = ofd.ShowDialog();
            if (dr == DialogResult.OK)
            {
                textBox1.Text = ofd.FileName;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (isWorkerThreadAlive == true)
            {
                MessageBox.Show("There are files currently being processed.");
                return;
            }
            ThreadStart job = new ThreadStart(doDecompression);
            Thread workerThread = new Thread(job);
            if (textBox1.Text.Length != 0 && textBox2.Text.Length != 0 && textBox3.Text.Length != 0)
            {
                button1.Enabled = false;
                button2.Enabled = false;
                button3.Enabled = false;
                button4.Enabled = false;
                workerThread.IsBackground = true;
                workerThread.Start();
            }
        }

        private void doDecompression()
        {
            isWorkerThreadAlive = true;
            uint unk = 0;
            int entries = 0;
            int progress = 0;
            var dtFiles = new List<dtFile>();

            using (var s = new FileStream(textBox1.Text, FileMode.Open))
            {
                using (var br = new BinaryReader(s))
                {
                    unk = br.ReadUInt32();
                    entries = br.ReadInt32();
                    progressBar1.Invoke((MethodInvoker)delegate { progressBar1.Maximum = 100; });
                    

                    for (int i = 0; i < entries; i++)
                    {
                        var file = new dtFile()
                        {
                            Hash = br.ReadUInt32(),
                            Offset = br.ReadUInt32(),
                            Size = br.ReadInt32()
                        };
                        dtFiles.Add(file);
                    }
                }
            }
            label2.Invoke((MethodInvoker)delegate { label2.Text = "Extracting..."; });
            Directory.CreateDirectory(textBox3.Text + "\\out");

            using (var s = new FileStream(textBox2.Text, FileMode.Open))
            {
                using (var br = new BinaryReader(s))
                {
                    for (int i = 0; i < entries; i++)
                    {
                        s.Seek(dtFiles[i].Offset, SeekOrigin.Begin);
                        dtFiles[i].Data = br.ReadBytes(dtFiles[i].Size);
                        string filename = String.Format(textBox3.Text + "\\out\\" + "{0:X8}.bin", dtFiles[i].Hash);
                        File.WriteAllBytes(filename, dtFiles[i].Data);
                        progress = 100 * i / entries;
                        progressBar1.Invoke((MethodInvoker)delegate { progressBar1.Value = progress; });
                        dtFiles[i].Data = null;
                        dtFiles[i].fileLocation = String.Format(textBox3.Text + "\\out\\" + "{0:X8}.bin", dtFiles[i].Hash);
                    }
                }
            }
            label2.Invoke((MethodInvoker)delegate { label2.Text = "Decompressing..."; });
            progressBar1.Invoke((MethodInvoker)delegate { progressBar1.Value = 0; });
            progress = 0;
            for (int i = 0; i < entries; i++)
            {
                FileStream fs2 = new FileStream(dtFiles[i].fileLocation, FileMode.Open);
                using (var fs = new MemoryStream())
                {
                    fs2.CopyTo(fs);
                    dtFiles[i].Data = fs.ToArray();
                    int ofs = 0;
                    while ((ofs < dtFiles[i].Data.Length - 2) && dtFiles[i].Data[ofs++] == 0xCC && dtFiles[i].Data[ofs++] == 0xCC) ;

                    if (ofs >= dtFiles[i].Data.Length - 2)
                        continue;

                    fs.Seek(ofs - 1, SeekOrigin.Begin);
                    var idx = 0;
                    do
                    {
                        Directory.CreateDirectory(String.Format(textBox3.Text + "\\out\\{0:X8}", dtFiles[i].Hash));
                        string filename = String.Format(textBox3.Text + "\\out\\{0:X8}\\{1}.bin", dtFiles[i].Hash, idx);
                        using (Stream fd = File.OpenWrite(filename))
                        {
                            var test = fs.ReadByte() << 8 | fs.ReadByte();
                            bool zlib = test == 0x789C;
                            fs.Seek(-2, SeekOrigin.Current);
                            int ofs2 = ofs;
                            while (!((dtFiles[i].Data[ofs2] == 0xCC) && (dtFiles[i].Data[ofs2 + 1] == 0xCC) && (dtFiles[i].Data[ofs2 + 2] == 0xCC) && (dtFiles[i].Data[ofs2 + 3] == 0xCC)))
                            {
                                if (ofs2 >= dtFiles[i].Data.Length - 4)
                                {
                                    ofs2 = dtFiles[i].Data.Length;
                                    while (dtFiles[i].Data[ofs2 - 1] == 0xCC)
                                        ofs2--;
                                    break;
                                }
                                ofs2++;
                            }
                            if (zlib) //Zlib
                            {
                                Stream fscopy = new MemoryStream(dtFiles[i].Data);
                                fscopy.Seek(fs.Position, SeekOrigin.Begin);
                                int len = ofs2 - (ofs - 1);
                                using (Stream csStream = new ZlibStream(fscopy, CompressionMode.Decompress))
                                {
                                    byte[] buffer = new byte[len];
                                    int nRead;
                                    while ((nRead = csStream.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        fd.Write(buffer, 0, nRead);
                                    }
                                }
                            }
                            else
                            {
                                int len = ofs2 - (ofs - 1);
                                byte[] buffer = new byte[len];
                                if (len == fs.Read(buffer, 0, len))
                                {
                                    fd.Write(buffer, 0, len);
                                }
                                else
                                {
                                    MessageBox.Show("BAD SHIT");
                                }
                            }
                        }

                        if (ofs >= dtFiles[i].Data.Length - 1) break;

                        while ((ofs < dtFiles[i].Data.Length - 4) && !((dtFiles[i].Data[ofs] == 0xCC) && (dtFiles[i].Data[ofs + 1] == 0xCC) && (dtFiles[i].Data[ofs + 2] == 0xCC) && (dtFiles[i].Data[ofs + 3] == 0xCC)))
                        {
                            ofs += 1;
                        }

                        while ((ofs < dtFiles[i].Data.Length - 2) && dtFiles[i].Data[ofs++] == 0xCC && dtFiles[i].Data[ofs++] == 0xCC) ;

                        if (ofs >= dtFiles[i].Data.Length - 2)
                            break;

                        fs.Seek(ofs - 1, SeekOrigin.Begin);
                        idx++;
                    }
                    while (fs.Position < fs.Length);
                }
                progress = 100 * i / entries;
                progressBar1.Invoke((MethodInvoker)delegate { progressBar1.Value = progress; });
                dtFiles[i].Data = null;
            }
            MessageBox.Show("Done!");
            label2.Invoke((MethodInvoker)delegate { label2.Text = "Done!"; });
            button1.Invoke((MethodInvoker)delegate { button1.Enabled = true; });
            button2.Invoke((MethodInvoker)delegate { button2.Enabled = true; });
            button3.Invoke((MethodInvoker)delegate { button3.Enabled = true; });
            button4.Invoke((MethodInvoker)delegate { button4.Enabled = true; });
            isWorkerThreadAlive = false;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (isWorkerThreadAlive == true)
            {
                DialogResult dialog = dialog = MessageBox.Show("There are files currently being processed. Do you really want to exit?", "Question", MessageBoxButtons.YesNo);
                if (dialog == DialogResult.No)
                {
                    e.Cancel = true;//cancel closing
                }
                else
                {
                    MessageBox.Show("File processing will now be stopped to close the application.");//messsage about file processing stopping.
                }
            }
        }
public static string GuessExtension(byte[] magic, string defaultExt)
        {
            string ext = "";
            for (int i = 0; i < magic.Length && i < 4; i++)
            {
                if ((magic[i] >= 'a' && magic[i] <= 'z') || (magic[i] >= 'A' && magic[i] <= 'Z' || (magic[i] <= '9' && magic[i] >= '0') || magic[i] == '_' || magic[i] == '-')
                    || char.IsDigit((char)magic[i]))
                {
                    ext += (char)magic[i];
                }
                else
                    break;
            }
            if (ext.Length <= 1)
                return defaultExt;
            return ext;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            byte[] magic = new Byte[4];
            string[] filepaths = Directory.GetFiles(textBox3.Text + "\\out\\", "*.*", SearchOption.AllDirectories);
            for (int i = 0; i < filepaths.Length; i++)
            {
                if (Directory.Exists(filepaths[i])) continue;
                try
                {
                    byte[] data = File.ReadAllBytes(filepaths[i]);
                    string s = filepaths[i];
                    Array.Copy(data, 0xC, magic, 0, 4);
                    string newstr = GuessExtension(magic,"");

                    if (newstr.Length == 4)
                    {
                        int ctr = 4;
                        while (true)
                        {
                            char c = (char)data[0xC + ctr];
                            if (newstr.Length > 100) break;
                            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c == '.') || (c == '_') || c == '-' || (c <= '9' && c >= '0'))
                            {
                                newstr += c;
                                ctr++;
                            }
                            else break;
                        }
                        if (newstr.Substring(newstr.Length - 4, 4) == ".tex")
                        {
                            string newpath = Path.Combine(Path.GetDirectoryName(filepaths[i]), newstr);
                            File.Move(filepaths[i], newpath);
                            continue;
                        }
                        else
                        {
                            Array.Copy(data, 0, magic, 0, 4);
                            string newname = Path.GetFileNameWithoutExtension(filepaths[i]) + "." + GuessExtension(magic, "bin");
                            File.Move(filepaths[i], Path.Combine(Path.GetDirectoryName(filepaths[i]), newname));
                        }
                    }
                    else 
                    {
                        Array.Copy(data, 0, magic, 0, 4);
                        string guess = GuessExtension(magic, "bin");
                        string newname = Path.GetFileNameWithoutExtension(filepaths[i]) + "." + guess;
                        File.Move(filepaths[i], Path.Combine(Path.GetDirectoryName(filepaths[i]), newname));
                    }
                }
                catch { }
            }
        }
    }
    class dtFile
    {
        public uint Hash { get; set; }
        public uint Offset { get; set; }
        public int Size { get; set; }
        public byte[] Data { get; set; }
        public string fileLocation { get; set; }
    }
}