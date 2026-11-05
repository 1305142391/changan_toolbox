namespace dipay_toolbox
{
    partial class FormMain
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
            this.button关闭服务 = new System.Windows.Forms.Button();
            this.button启动服务 = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.button读取APP签名 = new System.Windows.Forms.Button();
            this.button指定APP签名 = new System.Windows.Forms.Button();
            this.button设置APP安装 = new System.Windows.Forms.Button();
            this.button打开安卓设置 = new System.Windows.Forms.Button();
            this.buttonDiPlay卸载 = new System.Windows.Forms.Button();
            this.buttonDiPlay安装 = new System.Windows.Forms.Button();
            this.buttonVia浏览器卸载 = new System.Windows.Forms.Button();
            this.buttonVia浏览器安装 = new System.Windows.Forms.Button();
            this.button主页 = new System.Windows.Forms.Button();
            this.button返回 = new System.Windows.Forms.Button();
            this.button打开 = new System.Windows.Forms.Button();
            this.button导出 = new System.Windows.Forms.Button();
            this.button应用卸载 = new System.Windows.Forms.Button();
            this.button应用启用 = new System.Windows.Forms.Button();
            this.button应用停用 = new System.Windows.Forms.Button();
            this.button应用刷新 = new System.Windows.Forms.Button();
            this.listView应用列表 = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeader3 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.textBox日志 = new System.Windows.Forms.TextBox();
            this.groupBox4 = new System.Windows.Forms.GroupBox();
            this.button认证服务关闭 = new System.Windows.Forms.Button();
            this.button认证服务启动 = new System.Windows.Forms.Button();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.groupBox2.SuspendLayout();
            this.groupBox3.SuspendLayout();
            this.groupBox4.SuspendLayout();
            this.groupBox5.SuspendLayout();
            this.SuspendLayout();
            // 
            // button关闭服务
            // 
            this.button关闭服务.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.button关闭服务.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button关闭服务.Location = new System.Drawing.Point(107, 49);
            this.button关闭服务.Name = "button关闭服务";
            this.button关闭服务.Size = new System.Drawing.Size(99, 23);
            this.button关闭服务.TabIndex = 2;
            this.button关闭服务.Text = "关闭ADB服务";
            this.button关闭服务.UseVisualStyleBackColor = false;
            this.button关闭服务.Click += new System.EventHandler(this.button关闭服务_Click);
            // 
            // button启动服务
            // 
            this.button启动服务.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(128)))), ((int)(((byte)(255)))), ((int)(((byte)(128)))));
            this.button启动服务.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button启动服务.Location = new System.Drawing.Point(4, 49);
            this.button启动服务.Name = "button启动服务";
            this.button启动服务.Size = new System.Drawing.Size(99, 23);
            this.button启动服务.TabIndex = 1;
            this.button启动服务.Text = "启动ADB服务";
            this.button启动服务.UseVisualStyleBackColor = false;
            this.button启动服务.Click += new System.EventHandler(this.button启动服务_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.button读取APP签名);
            this.groupBox2.Controls.Add(this.button指定APP签名);
            this.groupBox2.Controls.Add(this.button设置APP安装);
            this.groupBox2.Controls.Add(this.button打开安卓设置);
            this.groupBox2.Controls.Add(this.buttonDiPlay卸载);
            this.groupBox2.Controls.Add(this.buttonDiPlay安装);
            this.groupBox2.Controls.Add(this.buttonVia浏览器卸载);
            this.groupBox2.Controls.Add(this.buttonVia浏览器安装);
            this.groupBox2.Location = new System.Drawing.Point(221, 12);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(427, 80);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "应用管理";
            // 
            // button读取APP签名
            // 
            this.button读取APP签名.Location = new System.Drawing.Point(216, 49);
            this.button读取APP签名.Name = "button读取APP签名";
            this.button读取APP签名.Size = new System.Drawing.Size(99, 23);
            this.button读取APP签名.TabIndex = 7;
            this.button读取APP签名.Text = "读取APK签名";
            this.button读取APP签名.UseVisualStyleBackColor = true;
            this.button读取APP签名.Click += new System.EventHandler(this.button读取APP签名_Click);
            // 
            // button指定APP签名
            // 
            this.button指定APP签名.Location = new System.Drawing.Point(111, 49);
            this.button指定APP签名.Name = "button指定APP签名";
            this.button指定APP签名.Size = new System.Drawing.Size(99, 23);
            this.button指定APP签名.TabIndex = 6;
            this.button指定APP签名.Text = "指定APK签名";
            this.button指定APP签名.UseVisualStyleBackColor = true;
            this.button指定APP签名.Click += new System.EventHandler(this.button指定APP签名_Click);
            // 
            // button设置APP安装
            // 
            this.button设置APP安装.Location = new System.Drawing.Point(6, 49);
            this.button设置APP安装.Name = "button设置APP安装";
            this.button设置APP安装.Size = new System.Drawing.Size(99, 23);
            this.button设置APP安装.TabIndex = 5;
            this.button设置APP安装.Text = "选择APK安装";
            this.button设置APP安装.UseVisualStyleBackColor = true;
            this.button设置APP安装.Click += new System.EventHandler(this.button设置APP安装_Click);
            // 
            // button打开安卓设置
            // 
            this.button打开安卓设置.Location = new System.Drawing.Point(321, 49);
            this.button打开安卓设置.Name = "button打开安卓设置";
            this.button打开安卓设置.Size = new System.Drawing.Size(99, 23);
            this.button打开安卓设置.TabIndex = 4;
            this.button打开安卓设置.Text = "打开原生设置";
            this.button打开安卓设置.UseVisualStyleBackColor = true;
            this.button打开安卓设置.Click += new System.EventHandler(this.button打开安卓设置_Click);
            // 
            // buttonDiPlay卸载
            // 
            this.buttonDiPlay卸载.Location = new System.Drawing.Point(321, 20);
            this.buttonDiPlay卸载.Name = "buttonDiPlay卸载";
            this.buttonDiPlay卸载.Size = new System.Drawing.Size(99, 23);
            this.buttonDiPlay卸载.TabIndex = 3;
            this.buttonDiPlay卸载.Text = "DiPlay卸载";
            this.buttonDiPlay卸载.UseVisualStyleBackColor = true;
            this.buttonDiPlay卸载.Click += new System.EventHandler(this.buttonDiPlay卸载_Click);
            // 
            // buttonDiPlay安装
            // 
            this.buttonDiPlay安装.Location = new System.Drawing.Point(216, 20);
            this.buttonDiPlay安装.Name = "buttonDiPlay安装";
            this.buttonDiPlay安装.Size = new System.Drawing.Size(99, 23);
            this.buttonDiPlay安装.TabIndex = 2;
            this.buttonDiPlay安装.Text = "DiPlay安装";
            this.buttonDiPlay安装.UseVisualStyleBackColor = true;
            this.buttonDiPlay安装.Click += new System.EventHandler(this.buttonDiPlay安装_Click);
            // 
            // buttonVia浏览器卸载
            // 
            this.buttonVia浏览器卸载.Location = new System.Drawing.Point(111, 20);
            this.buttonVia浏览器卸载.Name = "buttonVia浏览器卸载";
            this.buttonVia浏览器卸载.Size = new System.Drawing.Size(99, 23);
            this.buttonVia浏览器卸载.TabIndex = 1;
            this.buttonVia浏览器卸载.Text = "Via浏览器卸载";
            this.buttonVia浏览器卸载.UseVisualStyleBackColor = true;
            this.buttonVia浏览器卸载.Click += new System.EventHandler(this.buttonVia浏览器卸载_Click);
            // 
            // buttonVia浏览器安装
            // 
            this.buttonVia浏览器安装.Location = new System.Drawing.Point(6, 20);
            this.buttonVia浏览器安装.Name = "buttonVia浏览器安装";
            this.buttonVia浏览器安装.Size = new System.Drawing.Size(99, 23);
            this.buttonVia浏览器安装.TabIndex = 0;
            this.buttonVia浏览器安装.Text = "Via浏览器安装";
            this.buttonVia浏览器安装.UseVisualStyleBackColor = true;
            this.buttonVia浏览器安装.Click += new System.EventHandler(this.buttonVia浏览器安装_Click);
            // 
            // button主页
            // 
            this.button主页.BackColor = System.Drawing.Color.Silver;
            this.button主页.Location = new System.Drawing.Point(561, 197);
            this.button主页.Name = "button主页";
            this.button主页.Size = new System.Drawing.Size(75, 25);
            this.button主页.TabIndex = 16;
            this.button主页.Text = "主页";
            this.button主页.UseVisualStyleBackColor = false;
            this.button主页.Click += new System.EventHandler(this.button主页_Click);
            // 
            // button返回
            // 
            this.button返回.BackColor = System.Drawing.Color.Silver;
            this.button返回.Location = new System.Drawing.Point(480, 197);
            this.button返回.Name = "button返回";
            this.button返回.Size = new System.Drawing.Size(75, 25);
            this.button返回.TabIndex = 15;
            this.button返回.Text = "返回";
            this.button返回.UseVisualStyleBackColor = false;
            this.button返回.Click += new System.EventHandler(this.button返回_Click);
            // 
            // button打开
            // 
            this.button打开.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            this.button打开.Location = new System.Drawing.Point(561, 23);
            this.button打开.Name = "button打开";
            this.button打开.Size = new System.Drawing.Size(75, 25);
            this.button打开.TabIndex = 14;
            this.button打开.Text = "打开";
            this.button打开.UseVisualStyleBackColor = false;
            this.button打开.Click += new System.EventHandler(this.button打开_Click);
            // 
            // button导出
            // 
            this.button导出.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            this.button导出.Location = new System.Drawing.Point(480, 86);
            this.button导出.Name = "button导出";
            this.button导出.Size = new System.Drawing.Size(75, 25);
            this.button导出.TabIndex = 13;
            this.button导出.Text = "导出";
            this.button导出.UseVisualStyleBackColor = false;
            this.button导出.Click += new System.EventHandler(this.button导出_Click);
            // 
            // button应用卸载
            // 
            this.button应用卸载.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.button应用卸载.Location = new System.Drawing.Point(561, 86);
            this.button应用卸载.Name = "button应用卸载";
            this.button应用卸载.Size = new System.Drawing.Size(75, 25);
            this.button应用卸载.TabIndex = 12;
            this.button应用卸载.Text = "卸载";
            this.button应用卸载.UseVisualStyleBackColor = false;
            this.button应用卸载.Click += new System.EventHandler(this.button应用卸载_Click);
            // 
            // button应用启用
            // 
            this.button应用启用.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            this.button应用启用.Location = new System.Drawing.Point(561, 54);
            this.button应用启用.Name = "button应用启用";
            this.button应用启用.Size = new System.Drawing.Size(75, 25);
            this.button应用启用.TabIndex = 11;
            this.button应用启用.Text = "启用";
            this.button应用启用.UseVisualStyleBackColor = false;
            this.button应用启用.Click += new System.EventHandler(this.button应用启用_Click);
            // 
            // button应用停用
            // 
            this.button应用停用.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            this.button应用停用.Location = new System.Drawing.Point(480, 54);
            this.button应用停用.Name = "button应用停用";
            this.button应用停用.Size = new System.Drawing.Size(75, 25);
            this.button应用停用.TabIndex = 10;
            this.button应用停用.Text = "停用";
            this.button应用停用.UseVisualStyleBackColor = false;
            this.button应用停用.Click += new System.EventHandler(this.button应用停用_Click);
            // 
            // button应用刷新
            // 
            this.button应用刷新.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            this.button应用刷新.Location = new System.Drawing.Point(480, 23);
            this.button应用刷新.Name = "button应用刷新";
            this.button应用刷新.Size = new System.Drawing.Size(75, 25);
            this.button应用刷新.TabIndex = 9;
            this.button应用刷新.Text = "刷新";
            this.button应用刷新.UseVisualStyleBackColor = false;
            this.button应用刷新.Click += new System.EventHandler(this.button应用刷新_Click);
            // 
            // listView应用列表
            // 
            this.listView应用列表.AutoArrange = false;
            this.listView应用列表.BackColor = System.Drawing.SystemColors.Window;
            this.listView应用列表.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.listView应用列表.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1,
            this.columnHeader2,
            this.columnHeader3});
            this.listView应用列表.FullRowSelect = true;
            this.listView应用列表.GridLines = true;
            this.listView应用列表.HideSelection = false;
            this.listView应用列表.Location = new System.Drawing.Point(6, 20);
            this.listView应用列表.MultiSelect = false;
            this.listView应用列表.Name = "listView应用列表";
            this.listView应用列表.Size = new System.Drawing.Size(468, 202);
            this.listView应用列表.TabIndex = 8;
            this.listView应用列表.UseCompatibleStateImageBehavior = false;
            this.listView应用列表.View = System.Windows.Forms.View.Details;
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "应用名称";
            this.columnHeader1.Width = 100;
            // 
            // columnHeader2
            // 
            this.columnHeader2.Text = "应用包名";
            this.columnHeader2.Width = 220;
            // 
            // columnHeader3
            // 
            this.columnHeader3.Text = "来源";
            this.columnHeader3.Width = 100;
            // 
            // groupBox3
            // 
            this.groupBox3.Controls.Add(this.textBox日志);
            this.groupBox3.Location = new System.Drawing.Point(5, 332);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(643, 176);
            this.groupBox3.TabIndex = 2;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "日志";
            // 
            // textBox日志
            // 
            this.textBox日志.AcceptsReturn = true;
            this.textBox日志.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox日志.Location = new System.Drawing.Point(7, 20);
            this.textBox日志.MaxLength = 3276700;
            this.textBox日志.Multiline = true;
            this.textBox日志.Name = "textBox日志";
            this.textBox日志.ReadOnly = true;
            this.textBox日志.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox日志.Size = new System.Drawing.Size(629, 147);
            this.textBox日志.TabIndex = 0;

            // 
            // groupBox4
            // 
            this.groupBox4.BackColor = System.Drawing.SystemColors.Control;
            this.groupBox4.Controls.Add(this.button关闭服务);
            this.groupBox4.Controls.Add(this.button认证服务关闭);
            this.groupBox4.Controls.Add(this.button启动服务);
            this.groupBox4.Controls.Add(this.button认证服务启动);
            this.groupBox4.Location = new System.Drawing.Point(5, 12);
            this.groupBox4.Name = "groupBox4";
            this.groupBox4.Size = new System.Drawing.Size(210, 80);
            this.groupBox4.TabIndex = 3;
            this.groupBox4.TabStop = false;
            this.groupBox4.Text = "认证服务";
            // 
            // button认证服务关闭
            // 
            this.button认证服务关闭.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.button认证服务关闭.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button认证服务关闭.Location = new System.Drawing.Point(107, 20);
            this.button认证服务关闭.Name = "button认证服务关闭";
            this.button认证服务关闭.Size = new System.Drawing.Size(99, 23);
            this.button认证服务关闭.TabIndex = 2;
            this.button认证服务关闭.Text = "关闭认证服务";
            this.button认证服务关闭.UseVisualStyleBackColor = false;
            this.button认证服务关闭.Click += new System.EventHandler(this.button认证服务关闭_Click);
            // 
            // button认证服务启动
            // 
            this.button认证服务启动.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(128)))), ((int)(((byte)(255)))), ((int)(((byte)(128)))));
            this.button认证服务启动.Font = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button认证服务启动.Location = new System.Drawing.Point(4, 20);
            this.button认证服务启动.Name = "button认证服务启动";
            this.button认证服务启动.Size = new System.Drawing.Size(99, 23);
            this.button认证服务启动.TabIndex = 1;
            this.button认证服务启动.Text = "启动认证服务";
            this.button认证服务启动.UseVisualStyleBackColor = false;
            this.button认证服务启动.Click += new System.EventHandler(this.button认证服务启动_Click);
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.button主页);
            this.groupBox5.Controls.Add(this.listView应用列表);
            this.groupBox5.Controls.Add(this.button返回);
            this.groupBox5.Controls.Add(this.button应用刷新);
            this.groupBox5.Controls.Add(this.button打开);
            this.groupBox5.Controls.Add(this.button应用停用);
            this.groupBox5.Controls.Add(this.button导出);
            this.groupBox5.Controls.Add(this.button应用启用);
            this.groupBox5.Controls.Add(this.button应用卸载);
            this.groupBox5.Location = new System.Drawing.Point(5, 98);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(643, 228);
            this.groupBox5.TabIndex = 4;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "应用列表";
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(653, 510);
            this.Controls.Add(this.groupBox5);
            this.Controls.Add(this.groupBox4);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.groupBox2);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FormMain";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "长安车机助手";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FormMain_FormClosing);
            this.Load += new System.EventHandler(this.FormMain_Load);
            this.groupBox2.ResumeLayout(false);
            this.groupBox3.ResumeLayout(false);
            this.groupBox3.PerformLayout();
            this.groupBox4.ResumeLayout(false);
            this.groupBox5.ResumeLayout(false);
            this.ResumeLayout(false);

        }
        #endregion
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.TextBox textBox日志;
        private System.Windows.Forms.Button buttonVia浏览器安装;
        private System.Windows.Forms.Button button打开安卓设置;
        private System.Windows.Forms.Button buttonDiPlay卸载;
        private System.Windows.Forms.Button buttonDiPlay安装;
        private System.Windows.Forms.Button buttonVia浏览器卸载;
        private System.Windows.Forms.Button button指定APP签名;
        private System.Windows.Forms.Button button设置APP安装;
        private System.Windows.Forms.Button button关闭服务;
        private System.Windows.Forms.Button button启动服务;
        private System.Windows.Forms.GroupBox groupBox4;
        private System.Windows.Forms.Button button认证服务关闭;
        private System.Windows.Forms.Button button认证服务启动;
        private System.Windows.Forms.ListView listView应用列表;
        private System.Windows.Forms.ColumnHeader columnHeader1;
        private System.Windows.Forms.ColumnHeader columnHeader2;
        private System.Windows.Forms.ColumnHeader columnHeader3;
        private System.Windows.Forms.Button button应用启用;
        private System.Windows.Forms.Button button应用停用;
        private System.Windows.Forms.Button button应用刷新;
        private System.Windows.Forms.Button button应用卸载;
        private System.Windows.Forms.Button button打开;
        private System.Windows.Forms.Button button导出;
        private System.Windows.Forms.Button button主页;
        private System.Windows.Forms.Button button返回;
        private System.Windows.Forms.Button button读取APP签名;
        private System.Windows.Forms.GroupBox groupBox5;
    }
}
