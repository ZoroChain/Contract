﻿using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

namespace NFT_Token
{
    /// <summary>
    /// BlaCat合伙人证书 NFT
    /// </summary>
    public class PartnerToken : SmartContract
    {
        //管理员
        private static readonly byte[] superAdmin = Helper.ToScriptHash("AM5ho5nEodQiai1mCTFDV3YUNYApCorMCX");

        //BCT合约hash
        [Appcall("40a80749ef62da6fc3d74dbf6fc7745148922372")]
        static extern object bctCall(string method, object[] arr);

        [DisplayName("exchange")]
        public static event Action<byte[], byte[], byte[]> Exchanged;//(byte[] from, byte[] to, byte[] tokenId);

        [DisplayName("upgrade")]
        public static event Action<byte[], byte[], BigInteger, BigInteger> Upgraded;//(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank);

        [DisplayName("addpoint")]
        public static event Action<byte[], byte[], BigInteger> AddPointed; //(tokenID, address, point)

        [DisplayName("bind")]
        public static event Action<byte[], byte[]> Bound; //(address, tokenId)

        private static readonly byte[] Active = { };          //所有接口可用
        private static readonly byte[] Inactive = { 0x01 };   //只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        /// <summary>
        ///   NFT 合伙人证书合约
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        /// <param name="method">
        ///   接口名称
        /// </param>
        /// <param name="args">
        ///   参数列表
        /// </param>
        public static object Main(string method, object[] args)
        {
            var magicstr = "BlaCat Partner Certificate Token";
            if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;
                //管理员权限     设置合约状态          
                if (method == "setState")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    BigInteger setValue = (BigInteger)args[0];
                    if (setValue == 0)
                        Storage.Put(Context(), "state", Active);
                    if (setValue == 1)
                        Storage.Put(Context(), "state", Inactive);
                    if (setValue == 2)
                        Storage.Put(Context(), "state", AllStop);
                    return true;
                }

                //invoke
                if (method == "getState") return GetState();

                // stop 表示合约全部接口已停用
                if (GetState() == AllStop) return false;

                if (method == "getNftInfo") return GetNftByTokenId((byte[])args[0]);
                if (method == "getNftInfoById") return GetNftByTokenId((byte[])args[0]);
                if (method == "getTxinfo") return GetTxInfoByTxid((byte[])args[0]);
                if (method == "getGatherAddress") return Storage.Get(Context(), "gatherAddress");
                if (method == "getCount") return GetNftCount();

                // 以下接口只有 Active 时可用
                if (GetState() != Active) return false;

                if (method == "getConfig") return GetConfig();

                //设置参数
                if (method == "setConfig")
                {
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    BigInteger silverPrice = (BigInteger)args[0];
                    BigInteger goldPrice = (BigInteger)args[1];
                    BigInteger platinumPrice = (BigInteger)args[2];
                    BigInteger diamondPrice = (BigInteger)args[3];

                    BigInteger leaguerInvitePoint = (BigInteger)args[4];
                    BigInteger silverInvitePoint = (BigInteger)args[5];

                    BigInteger goldInvitePoint = (BigInteger)args[6];
                    BigInteger platinumInvitePoint = (BigInteger)args[7];
                    BigInteger diamondInvitePoint = (BigInteger)args[8];

                    BigInteger goldUpgradePoint = (BigInteger)args[9];
                    BigInteger platinumUpgradePoint = (BigInteger)args[10];
                    BigInteger diamondUpgradePoint = (BigInteger)args[11];

                    BigInteger twoLevelPointPercent = (BigInteger)args[12];
                    BigInteger threeLevelPointPercent = (BigInteger)args[13];

                    BigInteger holdDiamondPoint = (BigInteger)args[14];

                    var configs = new Config()
                    {
                        SilverPrice = silverPrice,
                        GoldPrice = goldPrice,
                        PlatinumPrice = platinumPrice,
                        DiamondPrice = diamondPrice,
                        LeaguerInvitePoint = leaguerInvitePoint,
                        SilverInvitePoint = silverInvitePoint,
                        GoldInvitePoint = goldInvitePoint,
                        PlatinumInvitePoint = platinumInvitePoint,
                        DiamondInvitePoint = diamondInvitePoint,
                        GoldUpgradePoint = goldUpgradePoint,
                        PlatinumUpgradePoint = platinumUpgradePoint,
                        DiamondUpgradePoint = diamondUpgradePoint,
                        TwoLevelPointPercent = twoLevelPointPercent,
                        ThreeLevelPointPercent = threeLevelPointPercent,
                        HoldDiamondPoint= holdDiamondPoint
                    };

                    byte[] configBytes = Helper.Serialize(configs);
                    Storage.Put(Context(), "config", configBytes);
                    return true;
                }

                Config config = GetConfig();
                if (config.SilverPrice == 0) throw new Exception("Not set config");

                //首次购买
                if (method == "buy") //(txid, inviterTokenId)
                {
                    if (args.Length != 2) return false;
                    return BuyNewNft(config, (byte[])args[0], (byte[])args[1]);
                }

                //绑定、需要用户签名
                if (method == "bind") //(address,tokenId)
                {
                    if (args.Length != 2) return false;
                    return BindNft((byte[])args[0], (byte[])args[1]);
                }

                //转手交易、需要用户签名
                if (method == "exchange")
                {
                    if (args.Length != 3) return false;
                    return ExchangeNft((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                }

                //升级
                if (method == "upgrade") //(txid, tokenId)
                {
                    if (args.Length != 2) return false;
                    return UpgradeNft((byte[])args[0], (byte[])args[1], config);
                }

                if (!Runtime.CheckWitness(superAdmin)) return false;

                if (method == "setGatherAddress")
                {
                    if (args.Length != 1) return false;
                    return SetGatherAddress((byte[])args[0]);
                }

                //第一本证书创世发行
                if (method == "deploy") //(address)
                {
                    if (args.Length != 1) return false;
                    return DeployFirstNft((byte[])args[0]);
                }

                //邀请普通会员加分
                if (method == "addPoint") //(tokenId)
                {
                    if (args.Length != 1) return false;
                    var nftInfo = GetNftByTokenId((byte[])args[0]);
                    if (nftInfo.Owner.Length != 20) return false;
                    return AddPoint(nftInfo, config.LeaguerInvitePoint);
                }

                //扣分
                if (method == "reducePoint") //(tokenId, pointValue)
                {
                    if (args.Length != 2) return false;
                    return ReducePoint((byte[])args[0], (BigInteger)args[1]);
                }

                //降级
                if (method == "reduceGrade")
                {
                    if (args.Length != 1) return false;
                    return ReduceGrade((byte[])args[0]);
                }

                #region 升级合约,耗费490,仅限管理员
                if (method == "updatecontract")
                {
                    //不是管理员 不能操作
                    if (!Runtime.CheckWitness(superAdmin)) return false;
                    if (args.Length != 1 && args.Length != 9) return false;

                    byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
                    byte[] new_script = (byte[])args[0];
                    //如果传入的脚本一样 不继续操作
                    if (script == new_script) return false;

                    byte[] parameter_list = new byte[] { 0x07, 0x10 };
                    byte return_type = 0x05;
                    bool need_storage = (bool)(object)05;
                    string name = "nft";
                    string version = "1.0";
                    string author = "ZoroChain";
                    string email = "0";
                    string description = "nft";

                    if (args.Length == 9)
                    {
                        parameter_list = (byte[])args[1];
                        return_type = (byte)args[2];
                        need_storage = (bool)args[3];
                        name = (string)args[4];
                        version = (string)args[5];
                        author = (string)args[6];
                        email = (string)args[7];
                        description = (string)args[8];
                    }
                    Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
                    return true;
                }
                #endregion
            }

            return false;
        }

        private static bool ReduceGrade(byte[] tokeId)
        {
            if (tokeId.Length != 32) return false;
            var nftInfo = GetNftByTokenId(tokeId);
            if (nftInfo.Owner.Length != 20) return false;
            if (nftInfo.Rank < 1) return false;
            nftInfo.Rank -= 1;
            SaveNftInfo(nftInfo);

            //(byte[] tokenId, byte[] owner, BigInteger lastRank, BigInteger nowRank)
            Upgraded(tokeId, nftInfo.Owner, nftInfo.Rank + 1, nftInfo.Rank);
            return true;
        }

        private static bool SetGatherAddress(byte[] gatherAdress)
        {
            if (gatherAdress.Length != 20) return false;
            Storage.Put(Context(), "gatherAddress", gatherAdress);
            return true;
        }

        private static bool ReducePoint(byte[] tokenId, BigInteger pointValue)
        {
            if (!Runtime.CheckWitness(superAdmin)) return false;
            if (tokenId.Length != 32) return false;

            NFTInfo nftInfo = GetNftByTokenId(tokenId);
            nftInfo.AvailablePoint -= pointValue;
            SaveNftInfo(nftInfo);

            //notify
            AddPointed(tokenId, nftInfo.Owner, 0 - pointValue);
            return false;
        }

        private static bool BindNft(byte[] address, byte[] tokenId)
        {
            if (!Runtime.CheckWitness(address)) return false;

            if (tokenId.Length != 32) return false;

            NFTInfo nftInfo = GetNftByTokenId(tokenId);

            var userTokenId = GetTokenIdByAddress(address);

            //重复绑定
            if (userTokenId == nftInfo.TokenId) return false;

            if (nftInfo.TokenId.Length != 32) return false;
            //不是自己的证书不能绑定
            if (nftInfo.Owner != address) return false;

            var key = UserNftKey(address);
            Storage.Put(Context(), key, nftInfo.TokenId);

            Bound(address, tokenId);

            return true;   
        }

        private static byte[] GetState() => Storage.Get(Context(), "state");

        private static bool DeployFirstNft(byte[] address)
        {
            if (address.Length != 20) return false;

            //判断初始发行是否已完成
            if (IsInitted()) return false;

            //构建一个证书
            var newNftInfo = CreateNft(address, null);

            //保存nft信息
            SaveNftInfo(newNftInfo);

            //增加数量
            AddNftCount(newNftInfo.Rank);

            //保存交易信息
            SaveTxInfo(null, address, newNftInfo.TokenId);

            //notify
            Exchanged(null, address, newNftInfo.TokenId);

            //标记初始发行完成
            Storage.Put(Context(), "initDeploy", 1);

            return true;
        }

        private static bool BuyNewNft(Config config, byte[] txid, byte[] inviterTokenId)
        {
            if (txid.Length != 32 || inviterTokenId.Length != 32) return false;

            var tx = GetBctTxInfo(txid);

            byte[] gatherAddress = Storage.Get(Context(), "gatherAddress");

            //钱没给够或收款地址对不上均false
            if (tx.@from.Length == 0 || tx.to.AsBigInteger() != gatherAddress.AsBigInteger() || tx.value < config.SilverPrice)
                return false;

            var nftInfo = CreateNft(tx.from, inviterTokenId);

            SaveNftInfo(nftInfo);

            //增加数量
            AddNftCount(nftInfo.Rank);

            GradedAddPoint(inviterTokenId, config.SilverInvitePoint);

            SaveTxInfo(null, tx.@from, nftInfo.TokenId);

            SetTxUsed(txid);

            //notify
            Exchanged(null, tx.@from, nftInfo.TokenId); 
            return true;
        }

        private static void GradedAddPoint(byte[] tokenId, BigInteger Point)
        {
            var firstLevelNftInfo = GetNftByTokenId(tokenId);
            if (firstLevelNftInfo.Owner.Length == 20)
            {
                //给一级上线加分
                AddPoint(firstLevelNftInfo, Point);
            }

            var twoLevelNftInfo = GetNftByTokenId(firstLevelNftInfo.InviterTokenId);
            if (twoLevelNftInfo.Owner.Length == 20)
            {
                //给二级上线加分
                AddPoint(twoLevelNftInfo, (Point * 20) / 100);
            }

            var threeLevelNftInfo = GetNftByTokenId(twoLevelNftInfo.InviterTokenId);
            if (threeLevelNftInfo.Owner.Length == 20)
            {
                //给三级上线加分
                AddPoint(threeLevelNftInfo, (Point * 5) / 100);
            }
        }

        private static bool UpgradeNft(byte[] txid, byte[] tokenId, Config config)
        {
            if (txid.Length != 32 || tokenId.Length != 32) return false;

            var tx = GetBctTxInfo(txid);

            byte[] gatherAddress = Storage.Get(Context(), "gatherAddress");

            if (tx.@from.Length != 20 || tx.to.AsBigInteger() != gatherAddress.AsBigInteger() || tx.value <= 0)
                return false;

            var nftInfo = GetNftByTokenId(tokenId);

            if (nftInfo.Owner != tx.from) return false;

            var reducePoint = UpgradeReducePoint(config, nftInfo, tx);

            if (reducePoint == 0) return false;
            //升级
            nftInfo.Rank += 1;
            //扣分
            nftInfo.AvailablePoint -= reducePoint;

            SaveNftInfo(nftInfo);

            //各等级数量变化
            AddNftCount(nftInfo.Rank);

            //升级给邀请者加分  获取要加的分数
            BigInteger addPoint = GetAddPoint(config, nftInfo.Rank);

            GradedAddPoint(nftInfo.InviterTokenId, addPoint);

            SetTxUsed(txid);
            //notify
            Upgraded(tokenId, tx.@from, nftInfo.Rank - 1, nftInfo.Rank);
            return true;
        }

        private static bool ExchangeNft(byte[] from, byte[] to, byte[] tokenId)
        {
            if (!Runtime.CheckWitness(from)) return false;
            if (from.Length != 20 || to.Length != 20 || tokenId.Length != 32) return false;

            var fromTokenId = GetTokenIdByAddress(from);
            //from 没有证书、false
            var fromNftInfo = GetNftByTokenId(tokenId);

            if (fromNftInfo.Owner != from) return false;

            //更换所有者
            fromNftInfo.Owner = to;

            SaveNftInfo(fromNftInfo);

            //如果把绑定的卖了、就删除
            if (fromTokenId == tokenId)
                DeleteAddressMap(from);

            SaveTxInfo(from, to, tokenId);
            //notify
            Exchanged(from, to, tokenId);

            return true;
        }

        private static bool AddPoint(NFTInfo nftInfo, BigInteger pointValue)
        {
            nftInfo.AllPoint += pointValue;
            nftInfo.AvailablePoint += pointValue;

            SaveNftInfo(nftInfo);
            //notify
            AddPointed(nftInfo.TokenId, nftInfo.Owner, pointValue);
            return true;
        }

        private static bool IsInitted()
        {
            byte[] deploy_data = Storage.Get(Context(), "initDeploy");
            if (deploy_data.Length != 0) return true;
            else return false;
        }

        private static void SaveAddressMap(byte[] address, byte[] tokenId)
        {
            var key = UserNftKey(address);
            Storage.Put(Context(), key, tokenId);
        }

        private static void DeleteAddressMap(byte[] address)
        {
            var key = UserNftKey(address);
            Storage.Delete(Context(), key);
        }

        private static byte[] GetTokenIdByAddress(byte[] address)
        {
            var key = UserNftKey(address);
            return Storage.Get(Context(), key);
        }

        public static BigInteger UpgradeReducePoint(Config config, NFTInfo nftInfo, TransferInfo tx)
        {
            if (nftInfo.Rank == 1 && nftInfo.AvailablePoint >= config.GoldUpgradePoint &&
                tx.value >= config.GoldPrice)
                return config.GoldUpgradePoint;
            if (nftInfo.Rank == 2 && nftInfo.AvailablePoint >= config.PlatinumUpgradePoint &&
                tx.value >= config.PlatinumPrice)
                return config.PlatinumUpgradePoint;
            if (nftInfo.Rank == 3 && nftInfo.AvailablePoint >= config.DiamondUpgradePoint &&
                tx.value >= config.DiamondPrice)
                return config.DiamondUpgradePoint;
            return 0;
        }

        public static BigInteger GetAddPoint(Config config, BigInteger rank)
        {
            if (rank == 2)
                return config.GoldInvitePoint;
            if (rank == 3)
                return config.PlatinumInvitePoint;
            if (rank == 4)
                return config.DiamondInvitePoint;
            return 0;
        }

        public static NFTInfo CreateNft(byte[] owner, byte[] inviterTokenId)
        { 
            var nftInfo = new NFTInfo();
            nftInfo.TokenId = Hash256((ExecutionEngine.ScriptContainer as Transaction).Hash);
            nftInfo.Owner = owner;
            nftInfo.AllPoint = 0;
            nftInfo.AvailablePoint = 0;
            nftInfo.Rank = 1;
            nftInfo.InviterTokenId = inviterTokenId;
            return nftInfo;
        }

        public static void AddNftCount(BigInteger rank)
        {
            var nftCount = GetNftCount();
            if (rank == 1)
            {
                nftCount.AllCount += 1;
                nftCount.SilverCount += 1;
            }
            if (rank == 2)
            {
                nftCount.GoldCount += 1;
                nftCount.SilverCount -= 1;
            }
            if (rank == 3)
            {
                nftCount.PlatinumCount += 1;
                nftCount.GoldCount -= 1;
            }
            if (rank == 4)
            {
                nftCount.DiamondCount += 1;
                nftCount.PlatinumCount -= 1;
            }
            var nftCountBytes = Helper.Serialize(nftCount);
            Storage.Put(Context(), "nftCount", nftCountBytes);
        }

        public static void SaveNftInfo(NFTInfo nftInfo)
        {
            var key = NftInfoKey(nftInfo.TokenId);
            byte[] nftInfoBytes = Helper.Serialize(nftInfo);
            Storage.Put(Context(), key, nftInfoBytes);
        }

        public static void SaveTxInfo(byte[] from, byte[] to, byte[] tokenId)
        {
            ExchangeInfo info = new ExchangeInfo();
            info.@from = from;
            info.to = to;
            info.tokenId = tokenId;
            byte[] exInfo = Helper.Serialize(info);
            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            var key = TxInfoKey(txid);
            Storage.Put(Context(), key, exInfo);
        }

        public static Config GetConfig()
        {
            var configBytes = Storage.Get(Context(), "config");
            if (configBytes.Length == 0)
                return new Config();
            return configBytes.Deserialize() as Config;
        }

        private static NftCount GetNftCount()
        {
            var data = Storage.Get(Context(), "nftCount");
            if (data.Length == 0)
                return new NftCount();
            return data.Deserialize() as NftCount;
        }

        public static NFTInfo GetNftByTokenId(byte[] tokenId)
        {
            var key = NftInfoKey(tokenId);
            byte[] data = Storage.Get(Context(), key);
            if (data.Length == 0)
                return new NFTInfo();
            return data.Deserialize() as NFTInfo;
        }

        public static ExchangeInfo GetTxInfoByTxid(byte[] txid)
        {
            var key = TxInfoKey(txid);
            var data = Storage.Get(Context(), key);
            if (data.Length == 0)
                return null;
            return data.Deserialize() as ExchangeInfo;
        }

        public static TransferInfo GetBctTxInfo(byte[] txid)
        {
            var key = BctTxidUsedKey(txid);
            var tInfo = new TransferInfo();
            var v = Storage.Get(Context(), key).AsBigInteger();
            if (v == 0)
            {
                object[] _p = new object[1] { txid };
                var info = bctCall("getTxInfo", _p);
                if (((object[])info).Length == 3)
                    tInfo = info as TransferInfo;
            }
            return tInfo;
        }

        public static void SetTxUsed(byte[] txid)
        {
            var key = BctTxidUsedKey(txid);
            Storage.Put(Context(), key, 1);
        }

        private static StorageContext Context() => Storage.CurrentContext;

        private static byte[] UserNftKey(byte[] address) => "userNft".AsByteArray().Concat(address);
        private static byte[] NftInfoKey(byte[] tokenId) => "nftInfo".AsByteArray().Concat(tokenId);
        private static byte[] TxInfoKey(byte[] txid) => "txInfo".AsByteArray().Concat(txid);
        private static byte[] BctTxidUsedKey(byte[] txid) => "bctTxidUsed".AsByteArray().Concat(txid);

    }

    public class ExchangeInfo
    {
        public byte[] from;
        public byte[] to;
        public byte[] tokenId;
    }

    public class TransferInfo
    {
        public byte[] from;
        public byte[] to;
        public BigInteger value;
    }

    //证书信息
    public class NFTInfo
    {
        public byte[] TokenId; //tokenid 证书ID
        public byte[] Owner; //所有者 address
        public BigInteger Rank; //等级
        public BigInteger AllPoint; //累积贡献值
        public BigInteger AvailablePoint; //可用贡献值
        public byte[] InviterTokenId; //邀请者证书ID
    }

    //配置
    public class Config
    {
        public BigInteger SilverPrice; //白银购买价格
        public BigInteger GoldPrice; //升级黄金价格
        public BigInteger PlatinumPrice; //升级铂金价格
        public BigInteger DiamondPrice; //升级钻石价格

        public BigInteger LeaguerInvitePoint; //邀请普通会员所得贡献值
        public BigInteger SilverInvitePoint; //邀请白银所得贡献值

        public BigInteger GoldInvitePoint; //被邀请者升级黄金时邀请者所得贡献值
        public BigInteger PlatinumInvitePoint; //被邀请者升级铂金时邀请者所得贡献值
        public BigInteger DiamondInvitePoint; //被邀请者升级钻石时邀请者所得贡献值

        public BigInteger GoldUpgradePoint; //升级黄金所需贡献值
        public BigInteger PlatinumUpgradePoint; //升级铂金所需贡献值
        public BigInteger DiamondUpgradePoint; //升级钻石所需贡献值

        public BigInteger TwoLevelPointPercent; //二级上线获得贡献度百分比
        public BigInteger ThreeLevelPointPercent; //三级上线获得贡献度百分比

        public BigInteger HoldDiamondPoint; //每年维持钻石所需贡献度
    }

    //已发行数量
    public class NftCount
    {
        public BigInteger AllCount; //总数量
        public BigInteger SilverCount; //白银数量
        public BigInteger GoldCount; //黄金数量
        public BigInteger PlatinumCount; //铂金数量
        public BigInteger DiamondCount; //钻石数量
    }
}