using System.Security.Cryptography;
using System;
using System.Windows.Forms;

namespace Winforms
{
    partial class WinForm_Infomation
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.TextBox_Legality = new System.Windows.Forms.TextBox();
            this.Label5 = new System.Windows.Forms.Label();
            this.TextBox_Mac = new System.Windows.Forms.TextBox();
            this.Label4 = new System.Windows.Forms.Label();
            this.TextBox_Ip = new System.Windows.Forms.TextBox();
            this.Label3 = new System.Windows.Forms.Label();
            this.TextBox2 = new System.Windows.Forms.TextBox();
            this.Label2 = new System.Windows.Forms.Label();
            this.TextBox1 = new System.Windows.Forms.TextBox();
            this.Label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // TextBox_Status
            // 
            this.TextBox_Legality.Enabled = false;
            this.TextBox_Legality.Location = new System.Drawing.Point(97, 180);
            this.TextBox_Legality.Name = "TextBox_Status";
            this.TextBox_Legality.Size = new System.Drawing.Size(144, 21);
            this.TextBox_Legality.TabIndex = 7;
            // 
            // Label5
            // 
            this.Label5.AutoSize = true;
            this.Label5.Location = new System.Drawing.Point(36, 184);
            this.Label5.Name = "Label5";
            this.Label5.Size = new System.Drawing.Size(29, 12);
            this.Label5.TabIndex = 2;
            this.Label5.Text = "状态";
            // 
            // TextBox_Mac
            // 
            this.TextBox_Mac.Enabled = false;
            this.TextBox_Mac.Location = new System.Drawing.Point(97, 142);
            this.TextBox_Mac.Name = "TextBox_Mac";
            this.TextBox_Mac.Size = new System.Drawing.Size(144, 21);
            this.TextBox_Mac.TabIndex = 8;
            // 
            // Label4
            // 
            this.Label4.AutoSize = true;
            this.Label4.Location = new System.Drawing.Point(36, 146);
            this.Label4.Name = "Label4";
            this.Label4.Size = new System.Drawing.Size(47, 12);
            this.Label4.TabIndex = 3;
            this.Label4.Text = "Mac地址";
            // 
            // TextBox_Ip
            // 
            this.TextBox_Ip.Enabled = false;
            this.TextBox_Ip.Location = new System.Drawing.Point(97, 104);
            this.TextBox_Ip.Name = "TextBox_Ip";
            this.TextBox_Ip.Size = new System.Drawing.Size(144, 21);
            this.TextBox_Ip.TabIndex = 9;
            // 
            // Label3
            // 
            this.Label3.AutoSize = true;
            this.Label3.Location = new System.Drawing.Point(36, 108);
            this.Label3.Name = "Label3";
            this.Label3.Size = new System.Drawing.Size(53, 12);
            this.Label3.TabIndex = 4;
            this.Label3.Text = "IP地址：";
            // 
            // TextBox2
            // 
            this.TextBox2.Enabled = false;
            this.TextBox2.Location = new System.Drawing.Point(97, 65);
            this.TextBox2.Name = "TextBox2";
            this.TextBox2.Size = new System.Drawing.Size(144, 21);
            this.TextBox2.TabIndex = 10;
            this.TextBox2.Text = "V1.0";
            // 
            // Label2
            // 
            this.Label2.AutoSize = true;
            this.Label2.Location = new System.Drawing.Point(36, 69);
            this.Label2.Name = "Label2";
            this.Label2.Size = new System.Drawing.Size(41, 12);
            this.Label2.TabIndex = 5;
            this.Label2.Text = "版本：";
            // 
            // TextBox1
            // 
            this.TextBox1.Enabled = false;
            this.TextBox1.Location = new System.Drawing.Point(97, 26);
            this.TextBox1.Name = "TextBox1";
            this.TextBox1.Size = new System.Drawing.Size(144, 21);
            this.TextBox1.TabIndex = 11;
            this.TextBox1.Text = "MIN";
            // 
            // Label1
            // 
            this.Label1.AutoSize = true;
            this.Label1.Location = new System.Drawing.Point(36, 30);
            this.Label1.Name = "Label1";
            this.Label1.Size = new System.Drawing.Size(41, 12);
            this.Label1.TabIndex = 6;
            this.Label1.Text = "作者：";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(279, 218);
            this.Controls.Add(this.TextBox_Legality);
            this.Controls.Add(this.Label5);
            this.Controls.Add(this.TextBox_Mac);
            this.Controls.Add(this.Label4);
            this.Controls.Add(this.TextBox_Ip);
            this.Controls.Add(this.Label3);
            this.Controls.Add(this.TextBox2);
            this.Controls.Add(this.Label2);
            this.Controls.Add(this.TextBox1);
            this.Controls.Add(this.Label1);
            this.Name = "Form1";
            this.Text = "信息";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        internal System.Windows.Forms.TextBox TextBox_Legality;
        internal System.Windows.Forms.Label Label5;
        internal System.Windows.Forms.TextBox TextBox_Mac;
        internal System.Windows.Forms.Label Label4;
        internal System.Windows.Forms.TextBox TextBox_Ip;
        internal System.Windows.Forms.Label Label3;
        internal System.Windows.Forms.TextBox TextBox2;
        internal System.Windows.Forms.Label Label2;
        internal System.Windows.Forms.TextBox TextBox1;
        internal System.Windows.Forms.Label Label1;

        public void AppendEventHandler(Object sender, EventArgs e)
        {
            ReceiveEventAargs msg = e as ReceiveEventAargs;
            if (msg != null)
            {
                TextBox_Ip.Text = msg.Ip;
                TextBox_Mac.Text = msg.Mac;
                TextBox_Legality.Text = msg.Legality.ToString();
            }
        } 




    }


    public class ReceiveEventAargs : EventArgs
    {
        public string Mac;
        public string Ip;
        public bool Legality;
    }
}

