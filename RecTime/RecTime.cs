﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using RecTimeLogic;
using AutoUpdaterDotNET;

namespace RecTime
{
    public partial class RecTime : MaterialForm
    {
        private InfoBackgroundWorker _infoWorker;
        private GoogleAnalyticsTracker _tracker;
        private StreamManager _infoResult;
        private int _previousLength = 0;
        private List<StreamBackgroundWorker> _workers = new List<StreamBackgroundWorker>(); 
        private Dictionary<MaterialRadioButton, StreamInfo> _streamButtons = new Dictionary<MaterialRadioButton, StreamInfo>();
        private bool _closing = false;

        #region Constructor 

        public RecTime()
        {
            InitializeComponent();
            this.StartPosition = FormStartPosition.CenterScreen;

            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.BlueGrey800, Primary.BlueGrey900, Primary.BlueGrey500, Accent.LightBlue200, TextShade.WHITE);

            string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            txtOutputLocation.Text = path;

            if (Properties.Settings.Default.UserId == Guid.Empty)
            {
                Properties.Settings.Default.UserId = Guid.NewGuid();
                Properties.Settings.Default.Save();
            }
            _tracker = new GoogleAnalyticsTracker(Application.ProductVersion, 
                Properties.Settings.Default.UserId.ToString());
        }

        #endregion

        #region Component Events

        private void numericLiveStartOffset_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LiveStartOffset = (int)numericLiveStartOffset.Value;
            Properties.Settings.Default.Save();
        }

        private void numericLiveStopOffset_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.LiveStopOffset = (int)numericLiveStopOffset.Value;
            Properties.Settings.Default.Save();
        }

        private void timerChannels_Tick(object sender, EventArgs e)
        {
            int startOffset = Properties.Settings.Default.LiveStartOffset;
            int stopOffset = Properties.Settings.Default.LiveStartOffset;

            _workers.OfType<ChannelRecorderBackgroundWorker>().Where(w => !w.IsBusy && !w.HasRun && 
                (w.Info.StartTime + TimeSpan.FromSeconds(startOffset)) <= DateTime.Now && (w.Info.StopTime + TimeSpan.FromSeconds(stopOffset)) >= DateTime.Now)
                .ToList().ForEach(w => w.StartAndUpdateOffsetTime(stopOffset));

            _workers.OfType<ChannelRecorderBackgroundWorker>().Where(w => (w.Info.StartTime + TimeSpan.FromSeconds(startOffset)) > DateTime.Now).
                ToList().ForEach(w => UpdateQueueStatus(w, "om " + (w.Info.StartTime + TimeSpan.FromSeconds(startOffset) - DateTime.Now).ToString(@"hh\:mm\:ss")));
        }

        private void btnChannel_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null)
                return;

            var form = new ChannelForm() {Channel = btn.Tag.ToString()};
            _workers.OfType<ChannelRecorderBackgroundWorker>().ToList().ForEach(w => form.QueuedPrograms.Add(w.Info));

            form.FormClosed += Form_FormClosed;
            form.Show();
        }

        private void Form_FormClosed(object sender, FormClosedEventArgs e)
        {
            var form = sender as ChannelForm;
            form.FormClosed -= Form_FormClosed;

            if (form.DialogResult == DialogResult.OK)
            {
                foreach (var program in form.SelectedPrograms)
                {
                    _tracker.SendEvent("Download " + form.Type, program.Title, null, 0);
                    var worker = new ChannelRecorderBackgroundWorker(form.Type, program, txtOutputLocation.Text);
                    
                    var filename = txtOutputLocation.Text + @"\" + worker.OutputFilename;
                    if (File.Exists(filename))
                    {
                        MessageBox.Show(this, "Filen finns redan: " + worker.OutputFilename, "Konflikt", MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                            continue;
                    }

                    //no dupes
                    if(_workers.OfType<ChannelRecorderBackgroundWorker>().Any(w => w.Info.Equals(program)))
                        continue;
                    _workers.Add(worker);

                    var data = new[] { program.Title, form.Type.ToString(), "0 %" };
                    var item = new ListViewItem(data) { Tag = worker };
                    listViewQueue.Items.Add(item);

                    worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
                    worker.ProgressChanged += Worker_ProgressChanged;
                }
            }
        }

        private void txtOutputLocation_Enter(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                txtOutputLocation.Text = folderBrowserDialog.SelectedPath;
            }
        }

        private void txtUrl_TextChanged(object sender, EventArgs e)
        {
            _infoResult = null;
            btnStartDownload.Visible = false;

            foreach (var kvp in _streamButtons)
            {
                kvp.Key.CheckedChanged -= Radio_CheckedChanged;
                tabPage1.Controls.Remove(kvp.Key);
            }
            _streamButtons.Clear();
            pictureBox.Image = null;

            bool validUrl = IsValidUrl(txtUrl.Text);

            if (Math.Abs(txtUrl.TextLength - _previousLength) > 1 && validUrl)
            {
                _infoWorker = new InfoBackgroundWorker(txtUrl.Text);
                _infoWorker.RunWorkerCompleted += _infoWorker_RunWorkerCompleted;
                _infoWorker.RunWorkerAsync();
            }
            _previousLength = txtUrl.TextLength;
        }

        private void btnStartDownload_Click(object sender, EventArgs e)
        {
            if (_infoResult != null)
            {
                _tracker.SendEvent("Download " +_infoResult.Type, _infoResult.LongTitle, _infoResult.BaseUrl, _infoResult.Duration);

                var filename = txtOutputLocation.Text + @"\" + txtFilename.Text;
                if (File.Exists(filename))
                {
                    MessageBox.Show(this, "File already exists", "File exists", MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                var worker = new StreamBackgroundWorker(_streamButtons.First(b => b.Key.Checked).Value,
                    txtOutputLocation.Text, txtFilename.Text);
                _workers.Add(worker);

                //Define
                var data = new []{_infoResult.Title, _infoResult.Type.ToString(), "0 %" };
                var item = new ListViewItem(data) { Tag = worker };
                listViewQueue.Items.Add(item);
                

                worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
                worker.ProgressChanged += Worker_ProgressChanged;

                if(!_workers.Any(w => w.IsBusy))
                    worker.RunWorkerAsync();
            }
        }

        private void materialTabControl1_Selected(object sender, TabControlEventArgs e)
        {
            _tracker.SendView(e.TabPage.Text);
        }

        private void txtUrl_Enter(object sender, EventArgs e)
        {
            txtUrl.Clear();
        }

        #endregion

        #region Worker events

        private void _infoWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _infoResult = _infoWorker.Manager;
            _infoWorker.RunWorkerCompleted -= _infoWorker_RunWorkerCompleted;
            _infoWorker = null;

            int i = 0;
            foreach (var streamInfo in _infoResult.Streams)
            {
                var radio = new MaterialRadioButton();
                radio.Text = streamInfo.ToString();
                radio.Size = new Size(250, 18);
                radio.Location = new Point(20, 122 + i * 20);
                radio.CheckedChanged += Radio_CheckedChanged;
                tabPage1.Controls.Add(radio);
                i++;
                _streamButtons.Add(radio, streamInfo);
            }

            if (_streamButtons.Count > 0)
            {
                txtFilename.Text = _infoResult.GetValidFileName(_infoResult.Streams.Last());
                pictureBox.Image = _infoResult.PosterImage;
                _streamButtons.Last().Key.Checked = true;
                btnStartDownload.Visible = true;
            }
            else
            {
                lblStatus.Text = "Status: Could not find stream";
            }
        }

        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            var radio = sender as MaterialRadioButton;
            if (sender == null || !_streamButtons.ContainsKey(radio) || !radio.Checked)
                return;

            txtFilename.Text = _infoResult.GetValidFileName(_streamButtons[radio]);
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            var infoData = e.UserState as FFmpegInfo;
            var worker = sender as StreamBackgroundWorker;

            if (infoData != null && worker != null)
            {
                TimeSpan timeNow;
                TimeSpan timeDuration;

                int workerCount = _workers.Count;
                int workerInProgress = _workers.Count(w => w.HasRun || w.IsBusy);
                var channel = worker as ChannelRecorderBackgroundWorker;

                if (TimeSpan.TryParse(infoData.Time, out timeNow) &&
                    TimeSpan.TryParse(worker.Duration, out timeDuration) && timeDuration.TotalSeconds > 0)
                {
                    string percentage = string.Format("{0:0.0 %}", (timeNow.TotalSeconds/timeDuration.TotalSeconds));
                    lblStatus.Text = string.Format("Status: {0,6} {1} ({2}/{3})",percentage, infoData, workerInProgress, workerCount);
                    UpdateQueueStatus(worker, percentage);
                }
                else
                    lblStatus.Text = string.Format("Status: {0} ({1}/{2})", infoData, workerInProgress, workerCount);


            }
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (_closing)
                return;

            ListViewItem queueItem = null;
            foreach (ListViewItem item in listViewQueue.Items)
            {
                if (item.Tag == sender)
                    queueItem = item;
            }

            if (e.Error != null)
            {
                MessageBox.Show(e.Error.ToString());
                if(queueItem != null)
                    queueItem.SubItems[2].Text = "Error";
            }
            else
            {
                if (queueItem != null)
                    queueItem.SubItems[2].Text = "Done";
            }

            lblStatus.Text = "Status: Done";

            var worker = _workers.FirstOrDefault(w => !w.IsBusy && !w.HasRun);
            worker?.RunWorkerAsync();
        }

        #endregion

        #region Form events

        private void RecTime_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_workers.Any(w => w.IsBusy) || _workers.OfType<ChannelRecorderBackgroundWorker>().Any(w => !w.HasRun))
            {
                if (
                    MessageBox.Show(this, "Du har pågående nedladdningar eller köade nedladdningar som inte är klara.\n\r\n\rVill du avsluta?" , "Ej klar", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) == DialogResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _closing = true;
            foreach (var streamBackgroundWorker in _workers)
            {
                if (streamBackgroundWorker.IsBusy)
                    streamBackgroundWorker.CancelAsync();
            }
        }

        private void RecTime_Activated(object sender, EventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                var data = (string)Clipboard.GetData(DataFormats.Text);
                if (IsValidUrl(data) && data != txtUrl.Text)
                {
                    _previousLength = 0;
                    txtUrl.Text = data;
                }

            }
        }

        private void RecTime_Load(object sender, EventArgs e)
        {
            AutoUpdater.Start("http://rectime.se/update/latest.xml");
            _tracker.SendView(materialTabControl1.TabPages[0].Text);
            materialLabelVersion.Text = "v." + Application.ProductVersion;

            numericLiveStartOffset.Value = Properties.Settings.Default.LiveStartOffset;
            numericLiveStopOffset.Value = Properties.Settings.Default.LiveStopOffset;
        }

        #endregion

        #region Private Helper Methods

        private void UpdateQueueStatus(StreamBackgroundWorker worker, string text)
        {
            foreach (ListViewItem item in listViewQueue.Items)
            {
                if (item.Tag == worker)
                    item.SubItems[2].Text = text;
            }
        }

        private bool IsValidUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            Uri uriResult;
            bool validUrl = Uri.TryCreate(url, UriKind.Absolute, out uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
            return (validUrl && UrlHelper.ParseUrl(url).Item1 != SourceType.Unknown);
        }

        #endregion

        #region PictureBox Events

        private void pictureBoxDonate_Click(object sender, EventArgs e)
        {
            string url = "";

            string business = "info@rectime.se";  
            string description = "Donation";          
            string country = "SE";                  // AU, US, etc.
            string currency = "SEK";                 // AUD, USD, etc.

            url += "https://www.paypal.com/cgi-bin/webscr" +
                "?cmd=" + "_donations" +
                "&business=" + business +
                "&lc=" + country +
                "&item_name=" + description +
                "&currency_code=" + currency +
                "&bn=" + "PP%2dDonationsBF";

            System.Diagnostics.Process.Start(url);
        }

        private void pictureBoxFFmpeg_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://ffmpeg.org");
        }

        #endregion

    }
}
