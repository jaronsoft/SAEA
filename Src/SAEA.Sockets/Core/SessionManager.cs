﻿/****************************************************************************
 * 
  ____    _    _____    _      ____             _        _   
 / ___|  / \  | ____|  / \    / ___|  ___   ___| | _____| |_ 
 \___ \ / _ \ |  _|   / _ \   \___ \ / _ \ / __| |/ / _ \ __|
  ___) / ___ \| |___ / ___ \   ___) | (_) | (__|   <  __/ |_ 
 |____/_/   \_\_____/_/   \_\ |____/ \___/ \___|_|\_\___|\__|
                                                             

*Copyright (c) 2018-2020 yswenli All Rights Reserved.
*CLR版本： 2.1.4
*机器名称：WENLI-PC
*公司名称：wenli
*命名空间：SAEA.Sockets
*文件名： SessionManager
*版本号： v5.0.0.1
*唯一标识：ef84e44b-6fa2-432e-90a2-003ebd059303
*当前的用户域：WENLI-PC
*创建人： yswenli
*电子邮箱：wenguoli_520@qq.com
*创建时间：2018/3/1 15:54:21
*描述：
*
*=====================================================================
*修改标记
*修改时间：2018/3/1 15:54:21
*修改人： yswenli
*版本号： v5.0.0.1
*描述：
*
*****************************************************************************/

using SAEA.Common;
using SAEA.Common.Caching;
using SAEA.Sockets.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;

namespace SAEA.Sockets.Core
{

    /// <summary>
    /// 会话管理器
    /// </summary>
    public class SessionManager
    {
        UserTokenPool _userTokenPool;

        MemoryCacheHelper<IUserToken> _session;

        TimeSpan _timeOut;

        private int _bufferSize = 1024 * 10;

        EventHandler<SocketAsyncEventArgs> _completed = null;

        BufferManager _bufferManager;

        SocketAsyncEventArgsPool _argsPool;

        /// <summary>
        /// 心跳过期事件
        /// </summary>
        public event Action<IUserToken> OnTimeOut;

        object _locker = new object();


        /// <summary>
        /// 构造会话管理器
        /// </summary>
        /// <param name="context"></param>
        /// <param name="bufferSize"></param>
        /// <param name="count"></param>
        /// <param name="completed"></param>
        public SessionManager(IContext context, int bufferSize, int count, EventHandler<SocketAsyncEventArgs> completed, TimeSpan timeOut)
        {
            _userTokenPool = new UserTokenPool(context, count);

            _session = new MemoryCacheHelper<IUserToken>();
            _timeOut = timeOut;
            _bufferSize = bufferSize;
            _completed = completed;

            _bufferManager = new BufferManager(_bufferSize * count, _bufferSize);
            _bufferManager.InitBuffer();

            _argsPool = new SocketAsyncEventArgsPool(count * 2);
            _argsPool.InitPool(_completed);

            //不存在时处理
            _session.OnChanged += _session_OnChanged;
        }

        private void _session_OnChanged(bool isAdd, IUserToken userToken)
        {
            if (!isAdd)
            {
                OnTimeOut?.Invoke(userToken);
            }
        }

        /// <summary>
        /// 初始化IUserToken
        /// </summary>
        /// <returns></returns>
        IUserToken InitUserToken()
        {
            IUserToken userToken = _userTokenPool.Dequeue();
            userToken.ReadArgs = _argsPool.Dequeue();
            _bufferManager.SetBuffer(userToken.ReadArgs);
            userToken.WriteArgs = _argsPool.Dequeue();
            userToken.ReadArgs.UserToken = userToken.WriteArgs.UserToken = userToken;
            return userToken;
        }

        /// <summary>
        /// 获取usertoken
        /// 如果IUserToken数量耗尽时可能会出现死锁，则需要外部使用
        /// </summary>
        /// <param name="socket"></param>
        /// <returns></returns>
        public IUserToken BindUserToken(Socket socket)
        {
            if (socket == null || socket.RemoteEndPoint == null) return null;

            IUserToken userToken = InitUserToken();
            userToken.Socket = socket;
            userToken.ID = socket.RemoteEndPoint.ToString();
            userToken.Actived = userToken.Linked = DateTimeHelper.Now;
            Set(userToken);
            return userToken;
        }


        void Set(IUserToken IUserToken)
        {
            _session.Set(IUserToken.ID, IUserToken, _timeOut);
        }

        public void Active(string ID)
        {
            _session.Active(ID, _timeOut);
        }

        public IUserToken Get(string ID)
        {
            return _session.Get(ID);
        }

        /// <summary>
        /// 释放IUserToken
        /// </summary>
        /// <param name="userToken"></param>
        public bool Free(IUserToken userToken)
        {
            if (userToken == null || userToken.Socket == null)
            {
                return false;
            }
            if (_session.Del(userToken.ID))
            {
                try
                {
                    if (userToken.Socket.Connected)
                    {
                        try
                        {
                            userToken.Socket.Shutdown(SocketShutdown.Both);
                        }
                        catch { }
                        userToken.Socket.Close();
                    }
                }
                catch { }
                _bufferManager.FreeBuffer(userToken.ReadArgs);
                _argsPool.Enqueue(userToken.ReadArgs);
                _argsPool.Enqueue(userToken.WriteArgs);
                _userTokenPool.Enqueue(userToken);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取全部会话
        /// </summary>
        /// <returns></returns>
        public List<IUserToken> ToList()
        {
            lock (_locker)
            {
                return _session.List.ToList();
            }
        }

        /// <summary>
        /// 清理全部会话
        /// </summary>
        public void Clear()
        {
            var list = ToList();
            foreach (var item in list)
            {
                item.Clear();
            }
            _session.Clear();
        }
    }
}
