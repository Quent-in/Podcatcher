﻿using System.Data.Linq.Mapping;
using System.ComponentModel;
using System;

namespace Podcatcher.ViewModels
{
    [Table]
    public class LastPlayedEpisodeModel
    {
        private int m_historyId;
        [Column(IsPrimaryKey = true, CanBeNull = false, IsDbGenerated = true)]
        public int LastPlayedID
        {
            get { return m_historyId; }
            set { m_historyId = value; }
        }

        [Column]
        public int LastPlayedEpisodeId
        {
            get;
            set;
        }

        [Column]
        public DateTime TimeStamp
        {
            get;
            set;
        }

#region propertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        public event PropertyChangingEventHandler PropertyChanging;
        
        private void NotifyPropertyChanging()
        {
            if ((this.PropertyChanging != null))
            {
                this.PropertyChanging(this, null);
            }
        }

        private void NotifyPropertyChanged(String propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (null != handler)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion
    }
}