﻿using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;

namespace BrokerContract
{
    public class BrokerContract : SmartContract
    {
        public delegate object NEP5Contract(string method, object[] args);

        // Events
        [DisplayName("created")]
        public static event Action<byte[], byte[], byte[], BigInteger, byte[], BigInteger> EmitCreated; // (address, offerHash, offerAssetID, offerAmount, wantAssetID, wantAmount)

        [DisplayName("filled")]
        public static event Action<byte[], byte[], BigInteger, byte[], BigInteger, byte[], BigInteger, BigInteger> EmitFilled; // (address, offerHash, fillAmount, offerAssetID, offerAmount, wantAssetID, wantAmount, amountFillerGet)

        [DisplayName("fillFailed")]
        public static event Action<byte[], byte[], BigInteger, byte[], BigInteger, byte[]> EmitFillFailed; // (address, offerHash, fillAmount, takerFeeAsssetID, takerFee, reason)

        [DisplayName("withdrawnFailed")]
        public static event Action<byte[], byte[], BigInteger, byte[], BigInteger, byte[]> EmitWithdrawnFailed; // (address, withdrawAssetID, withdrawAmount, feeAssetId, takerFee, reason)

        [DisplayName("cancelled")]
        public static event Action<byte[], byte[]> EmitCancelled; // (address, offerHash)

        [DisplayName("transferred")]
        public static event Action<byte[], byte[], BigInteger, byte[]> EmitTransferred; // (address, assetID, amount, reason)

        [DisplayName("deposited")]
        public static event Action<byte[], byte[], BigInteger> EmitDeposited; // (address, assetID, amount)

        [DisplayName("withdrawn")]
        public static event Action<byte[], byte[], BigInteger> EmitWithdrawn; // (address, assetID, amount, utxoUsed)

        [DisplayName("addedToWhitelist")]
        public static event Action<byte[]> EmitAddedToWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("removedFromWhitelist")]
        public static event Action<byte[]> EmitRemovedFromWhitelist; // (scriptHash, whitelistEnum)

        [DisplayName("feeAddressSet")]
        public static event Action<byte[]> EmitFeeAddressSet; // (address)

        [DisplayName("dealerAddressSet")]
        public static event Action<byte[]> EmitDealerAddressSet; // (address)

        [DisplayName("initialized")]
        public static event Action Initialized;

        // Broker Settings & Hardcaps
        private static readonly byte[] superAdmin = "AGZqPBPbkGoVCQTGSpcyBZRSWJmvdbPD2s".ToScriptHash();

        // Contract States
        private static readonly byte[] Active = { };       // 所有接口可用
        private static readonly byte[] Inactive = { 0x01 };//只有 invoke 可用
        private static readonly byte[] AllStop = { 0x02 };    //全部接口停用

        // Byte Constants
        private static readonly byte[] Empty = { };
        private static readonly byte[] Zero = { 0x00 };

        // Reason Code for balance changes
        private static readonly byte[] ReasonDeposit = { 0x01 }; // Balance increased due to deposit
        private static readonly byte[] ReasonMake = { 0x02 }; // Balance reduced due to maker making
        private static readonly byte[] ReasonTake = { 0x03 }; // Balance reduced due to taker filling maker's offered asset
        private static readonly byte[] ReasonTakerFee = { 0x04 }; // Balance reduced due to taker fees
        private static readonly byte[] ReasonTakerReceive = { 0x05 }; // Balance increased due to taker receiving his cut in the trade
        private static readonly byte[] ReasonMakerReceive = { 0x06 }; // Balance increased due to maker receiving his cut in the trade
        private static readonly byte[] ReasonTakerFeeReceive = { 0x07 }; // Balance increased on fee address due to contract receiving taker fee
        private static readonly byte[] ReasonCancel = { 0x08 }; // Balance increased due to cancelling offer
        private static readonly byte[] ReasonCancelFeeReceive = { 0x09 };
        private static readonly byte[] ReasonCancelFee = { 0x10 };
        private static readonly byte[] ReasonWithdrawFee = { 0x11 };
        private static readonly byte[] ReasonWithdraw = { 0x12 };
        private static readonly byte[] ReasonWithdrawFeeReceive = { 0x13 };

        // Reason Code for fill failures
        private static readonly byte[] ReasonOfferNotExist = { 0x21 }; // Empty Offer when trying to fill
        private static readonly byte[] ReasonTakingLessThanOne = { 0x22 }; // Taking less than 1 asset when trying to fill
        private static readonly byte[] ReasonFillerSameAsMaker = { 0x23 }; // Filler same as maker
        private static readonly byte[] ReasonTakingMoreThanAvailable = { 0x24 }; // Taking more than available in the offer at the moment
        private static readonly byte[] ReasonFillingLessThanOne = { 0x25 }; // Filling less than 1 asset when trying to fill
        private static readonly byte[] ReasonNotEnoughBalanceOnFiller = { 0x26 }; // Not enough balance to give (wantAssetID) for what you want to take (offerAssetID)
        private static readonly byte[] ReasonNotEnoughBalanceToken = { 0x27 }; // Not enough balance in native tokens to use
        private static readonly byte[] ReasonNotEnoughFee = { 0x28 }; // Fees exceed 0.5%

        private class Offer
        {
            public byte[] MakerAddress;
            public byte[] OfferAssetID;
            public BigInteger OfferAmount;
            public byte[] WantAssetID;
            public BigInteger WantAmount;
            public BigInteger AvailableAmount;
            public byte[] FeeAeestId;
            public BigInteger FeeAmount;
        }

        private static Offer NewOffer(
            byte[] makerAddress,
            byte[] offerAssetID, BigInteger offerAmount,
            byte[] wantAssetID, BigInteger wantAmount,
            byte[] feeAeestId, BigInteger feeAmount
        )
        {
            return new Offer
            {
                MakerAddress = makerAddress,
                OfferAssetID = offerAssetID,
                OfferAmount = offerAmount,
                WantAssetID = wantAssetID,
                WantAmount = wantAmount,
                AvailableAmount = offerAmount,
                FeeAeestId = feeAeestId,
                FeeAmount = feeAmount
            };
        }

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        /// <summary>
        ///   This is the Switcheo smart contract entrypoint.
        ///
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        /// <param name="operation">
        ///   The method to be invoked.
        /// </param>
        /// <param name="args">
        ///   Input parameters for the delegated method.
        /// </param>
        public static object Main(string operation, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (GetState() == AllStop) return false;

                // == Getters ==
                if (operation == "getState") return GetState();
                if (operation == "getOffer") return GetOffer((byte[])args[0]); //offerHash
                if (operation == "getBalance") return GetBalance((byte[])args[0], (byte[])args[1]); //address, assetID
                if (operation == "getIsWhitelisted") return GetIsWhitelisted((byte[])args[0]);  // (assetID)
                if (operation == "getFeeAddress") return GetFeeAddress(); //收交易费账户

                if (GetState() != Active) return false;

                // == Execute ==
                if (operation == "deposit") // (originator, assetID, txid) 存钱，如果不能跳板调用的话需要支持 txid 存钱
                {
                    if (args.Length != 3) return false;
                    return Deposit((byte[])args[0], (byte[])args[1], (byte[])args[2]);
                }
                //挂单
                if (operation == "makeOffer") // (makerAddress, offerAssetID, offerAmount, wantAssetID, wantAmount, feeAssetID, feeAmount)
                {
                    if (args.Length != 7) return false;
                    var offer = NewOffer((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3], (BigInteger)args[4], (byte[])args[5], (BigInteger)args[6]);
                    return MakeOffer(offer);
                }
                //撮合成交
                if (operation == "fillOffer") // fillerAddress, offerHash, amountToTake, takerFeeAssetID, takerFeeAmount)
                {
                    if (args.Length != 5) return false;
                    return FillOffer((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3], (BigInteger)args[4]);
                }
                //取消报单
                if (operation == "cancelOffer") // (offerHash)
                {
                    if (args.Length != 1) return false;
                    return CancelOffer((byte[])args[0]);
                }
                //取钱
                if (operation == "withdraw") // originator, withdrawAssetId, withdrawAmount, feeAssetId, feeAmount
                {
                    if (args.Length != 5) return false;
                    return Withdrawal((byte[])args[0], (byte[])args[1], (BigInteger)args[2], (byte[])args[3], (BigInteger)args[4]);
                }

                // == Owner ==
                if (!Runtime.CheckWitness(superAdmin))
                {
                    Runtime.Log("Owner signature verification failed");
                    return false;
                }
                //管理员权限     设置合约状态          
                if (operation == "setState")
                {
                    if (args.Length != 1) return false;
                    return SetState((BigInteger)args[0]);
                }
                // == Init == 设置交易费收取地址,设置交易员
                if (operation == "initialize")
                {
                    if (args.Length != 2) return false;
                    return Initialize((byte[])args[0], (byte[])args[1]);
                }
                if (operation == "setDealerAddress")
                {
                    if (args.Length != 1) return false;
                    return SetDealerAddress((byte[])args[0]);
                }
                if (operation == "setFeeAddress")
                {
                    if (args.Length != 1) return false;
                    return SetFeeAddress((byte[])args[0]);
                }
                if (operation == "addToWhitelist")
                {
                    if (args.Length != 1) return false;
                    return AddToWhitelist((byte[])args[0]);
                }
                if (operation == "removeFromWhitelist")
                {
                    if (args.Length != 1) return false;
                    return RemoveFromWhitelist((byte[])args[0]);
                }
            }

            return true;
        }

        /***********
         * Getters *
         ***********/

        private static byte[] GetState()
        {
            return Storage.Get(Context(), "state");
        }

        private static BigInteger GetBalance(byte[] address, byte[] assetID)
        {
            if (address.Length != 20) throw new ArgumentOutOfRangeException();
            if (assetID.Length != 20 && assetID.Length != 32) throw new ArgumentOutOfRangeException();
            return Storage.Get(Context(), BalanceKey(address, assetID)).AsBigInteger();
        }

        private static Offer GetOffer(byte[] offerHash)
        {
            if (offerHash.Length != 32) throw new ArgumentOutOfRangeException();

            byte[] offerData = Storage.Get(Context(), OfferKey(offerHash));
            if (offerData.Length == 0) return new Offer();

            return (Offer)offerData.Deserialize();
        }

        private static byte[] GetFeeAddress()
        {
            return Storage.Get(Context(), "feeAddress");
        }

        private static byte[] GetDealerAddress()
        {
            return Storage.Get(Context(), "dealerAddress");
        }

        private static bool GetIsWhitelisted(byte[] assetID)
        {
            if (assetID.Length != 20) return false;
            if (Storage.Get(Context(), WhitelistKey(assetID)).AsBigInteger() == 1)
                return true;
            return false;
        }

        /***********
         * Control *
         ***********/

        private static bool SetState(BigInteger setValue)
        {
            if (setValue == 0)
                Storage.Put(Context(), "state", Active);
            if (setValue == 1)
                Storage.Put(Context(), "state", Inactive);
            if (setValue == 2)
                Storage.Put(Context(), "state", AllStop);
            return true;
        }

        private static bool Initialize(byte[] feeAddress, byte[] dealerAddress)
        {
            if (!SetFeeAddress(feeAddress)) throw new Exception("Failed to set fee address");
            if (!SetDealerAddress(dealerAddress)) throw new Exception("Failed to set the dealer address");
            Initialized();
            return true;
        }

        private static bool SetFeeAddress(byte[] feeAddress)
        {
            if (feeAddress.Length != 20) return false;
            Storage.Put(Context(), "feeAddress", feeAddress);
            EmitFeeAddressSet(feeAddress);
            return true;
        }

        private static bool SetDealerAddress(byte[] dealerAddress)
        {
            if (dealerAddress.Length != 20) return false;
            Storage.Put(Context(), "dealerAddress", dealerAddress);
            EmitDealerAddressSet(dealerAddress);
            return true;
        }

        private static bool AddToWhitelist(byte[] scriptHash)
        {
            if (scriptHash.Length != 20) return false;
            var key = WhitelistKey(scriptHash);
            Storage.Put(Context(), key, 1);
            EmitAddedToWhitelist(scriptHash);
            return true;
        }

        private static bool RemoveFromWhitelist(byte[] scriptHash)
        {
            if (scriptHash.Length != 20) return false;
            var key = WhitelistKey(scriptHash);
            Storage.Delete(Context(), key);
            EmitRemovedFromWhitelist(scriptHash);
            return true;
        }

        /***********
         * Trading *
         ***********/

        private static bool MakeOffer(Offer offer)
        {
            // Check that transaction is signed by the maker and coordinator
            if (!Runtime.CheckWitness(offer.MakerAddress)) return false;

            // Check that nonce is not repeated
            var offerHash = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            if (Storage.Get(Context(), OfferKey(offerHash)) != Empty) return false;

            // Check that the amounts > 0
            if (!(offer.OfferAmount > 0 && offer.WantAmount > 0 && offer.FeeAmount > 0)) return false;

            // Check the trade is across different assets
            if (offer.OfferAssetID == offer.WantAssetID) return false;

            // Check that asset IDs are valid
            if (offer.OfferAssetID.Length != 20 || offer.WantAssetID.Length != 20 || offer.FeeAeestId.Length != 20) return false;

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            bool deductFeesSeparately = offer.FeeAeestId != offer.OfferAssetID;

            if (deductFeesSeparately)
            {
                // Check that there is enough balance in native fees if using native fees
                if (GetBalance(offer.MakerAddress, offer.FeeAeestId) < offer.FeeAmount) return false;
                // Reduce fee
                if (!ReduceBalance(offer.MakerAddress, offer.FeeAeestId, offer.FeeAmount, ReasonTakerFee)) return false;
            }

            // Reduce available balance for the offered asset and amount
            if (!ReduceBalance(offer.MakerAddress, offer.OfferAssetID, offer.OfferAmount, ReasonMake)) return false;

            //add fee
            IncreaseBalance(feeAddress, offer.FeeAeestId, offer.FeeAmount, ReasonTakerFeeReceive);

            if (!deductFeesSeparately)
            {
                offer.OfferAmount -= offer.FeeAmount;
                offer.AvailableAmount -= offer.FeeAmount;
            }

            // Add the offer to storage
            StoreOffer(offerHash, offer);

            // Notify clients
            EmitCreated(offer.MakerAddress, offerHash, offer.OfferAssetID, offer.OfferAmount, offer.WantAssetID, offer.WantAmount);
            return true;
        }

        // Fills an offer by taking the amount you want
        // => amountToFill's asset type = offer's wantAssetID
        // amountToTake's asset type = offerAssetID (taker is taking what is offered)
        private static bool FillOffer(byte[] fillerAddress, byte[] offerHash, BigInteger fillAmount, byte[] takerFeeAssetID, BigInteger takerFeeAmount)
        {
            // Note: We do all checks first then execute state changes
            if (!Runtime.CheckWitness(GetDealerAddress())) return false;
            // Check fees
            if (takerFeeAssetID.Length != 20) return false;
            if (takerFeeAmount < 0) return false;

            // Check that the offer still exists
            Offer offer = GetOffer(offerHash);
            if (offer.MakerAddress == Empty)
            {
                EmitFillFailed(fillerAddress, offerHash, fillAmount, takerFeeAssetID, takerFeeAmount, ReasonOfferNotExist);
                return false;
            }

            // Check that the filler is different from the maker
            if (fillerAddress == offer.MakerAddress)
            {
                EmitFillFailed(fillerAddress, offerHash, fillAmount, takerFeeAssetID, takerFeeAmount, ReasonFillerSameAsMaker);
                return false;
            }

            // Check that the amount that will be taken is at least 1
            if (fillAmount < 1)
            {
                EmitFillFailed(fillerAddress, offerHash, fillAmount, takerFeeAssetID, takerFeeAmount, ReasonTakingLessThanOne);
                return false;
            }

            // Check that there is enough balance to reduce for filler
            var fillerBalance = GetBalance(fillerAddress, offer.WantAssetID);

            if (fillerBalance < fillAmount)
            {
                EmitFillFailed(fillerAddress, offerHash, fillAmount, takerFeeAssetID, takerFeeAmount, ReasonNotEnoughBalanceOnFiller);
                return false;
            }

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            // 如果 takerFeeAssetID != offer.OfferAssetID，交易费需要单独扣
            bool deductFeesSeparately = takerFeeAssetID != offer.WantAssetID;

            // Calculate amount we have to give the filler, fillAmount * (offer.OfferAmount / offer.WantAmount)
            BigInteger amountFillerGet = (fillAmount * offer.OfferAmount) / offer.WantAmount;

            if (!deductFeesSeparately)
            {
                amountFillerGet = ((fillAmount - takerFeeAmount) * offer.OfferAmount) / offer.WantAmount;
            }

            // Check that amount to fill(give) is not less than 1
            if (amountFillerGet < 1)
            {
                EmitFillFailed(fillerAddress, offerHash, fillAmount, takerFeeAssetID, takerFeeAmount, ReasonTakingLessThanOne);
                return false;
            }

            // Check that you cannot take more than available
            if (amountFillerGet > offer.AvailableAmount)
            {
                EmitFillFailed(fillerAddress, offerHash, fillAmount, takerFeeAssetID, takerFeeAmount, ReasonTakingMoreThanAvailable);
                return false;
            }

            // Check that there is enough balance in native fees if using native fees
            if (GetBalance(fillerAddress, takerFeeAssetID) < takerFeeAmount)
            {
                EmitFillFailed(fillerAddress, offerHash, fillAmount, takerFeeAssetID, takerFeeAmount, ReasonNotEnoughFee);
                return false;
            }

            if (deductFeesSeparately)
            {
                //add offer.MakerAddress amount
                IncreaseBalance(offer.MakerAddress, offer.WantAssetID, fillAmount, ReasonMakerReceive);

                // Reduce fees here separately as it is a different asset type
                ReduceBalance(fillerAddress, takerFeeAssetID, takerFeeAmount, ReasonTakerFee);
            }
            else
            {
                //add offer.MakerAddress amount
                IncreaseBalance(offer.MakerAddress, offer.WantAssetID, fillAmount - takerFeeAmount, ReasonMakerReceive);
            }

            // Reduce balance from filler
            ReduceBalance(fillerAddress, offer.WantAssetID, fillAmount, ReasonTake);

            //add fee
            IncreaseBalance(feeAddress, takerFeeAssetID, takerFeeAmount, ReasonTakerFeeReceive);

            //add fillerAddress amount
            IncreaseBalance(fillerAddress, offer.OfferAssetID, amountFillerGet, ReasonTakerReceive);

            // Update available amount
            offer.AvailableAmount = offer.AvailableAmount - amountFillerGet;

            // Store updated offer
            StoreOffer(offerHash, offer);

            // Notify clients
            // (address, offerHash, fillAmount, offerAssetID, offerAmount, wantAssetID, wantAmount, amountFillerGet)
            EmitFilled(fillerAddress, offerHash, fillAmount, offer.OfferAssetID, offer.OfferAmount, offer.WantAssetID, offer.WantAmount, amountFillerGet);

            return true;
        }

        private static bool CancelOffer(byte[] offerHash)
        {
            // Check that the offer exists
            Offer offer = GetOffer(offerHash);
            if (offer.MakerAddress == Empty) return false;

            // Check that transaction is signed by the canceller or trading is frozen 是否冻结交易接口
            if (!(Runtime.CheckWitness(offer.MakerAddress))) return false;

            //按比例计算剩余的交易费
            var feeAmount = (offer.AvailableAmount * offer.FeeAmount) / offer.OfferAmount;

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");
            if (feeAddress.Length != 20) return false;

            var feeAddressBalance = GetBalance(feeAddress, offer.WantAssetID);

            if (feeAddressBalance < feeAmount) return false;

            //add fee to MakerAddress
            IncreaseBalance(offer.MakerAddress, offer.FeeAeestId, feeAmount, ReasonCancelFeeReceive);

            //reduce fee from feeAddress
            ReduceBalance(feeAddress, offer.FeeAeestId, feeAmount, ReasonCancelFee);

            // Move funds to maker address
            IncreaseBalance(offer.MakerAddress, offer.OfferAssetID, offer.AvailableAmount, ReasonCancel);

            // Remove offer
            Storage.Delete(Context(), OfferKey(offerHash));

            // Notify runtime
            EmitCancelled(offer.MakerAddress, offerHash);
            return true;
        }

        private static void StoreOffer(byte[] offerHash, Offer offer)
        {
            if (offer.AvailableAmount < 0)
            {
                throw new Exception("Invalid offer available amount!");
            }
            // Store offer otherwise
            else
            {
                // Serialize offer
                var offerData = offer.Serialize();
                Storage.Put(Context(), OfferKey(offerHash), offerData);
            }
        }

        private static bool IncreaseBalance(byte[] originator, byte[] assetID, BigInteger amount, byte[] reason)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            byte[] key = BalanceKey(originator, assetID);
            BigInteger currentBalance = Storage.Get(Context(), key).AsBigInteger();
            Storage.Put(Context(), key, currentBalance + amount);
            EmitTransferred(originator, assetID, amount, reason);

            return true;
        }

        private static bool ReduceBalance(byte[] address, byte[] assetID, BigInteger amount, byte[] reason)
        {
            if (amount < 1) throw new ArgumentOutOfRangeException();

            var key = BalanceKey(address, assetID);
            var currentBalance = Storage.Get(Context(), key).AsBigInteger();
            var newBalance = currentBalance - amount;

            if (newBalance < 0) return false;

            if (newBalance > 0) Storage.Put(Context(), key, newBalance);
            else Storage.Delete(Context(), key);
            EmitTransferred(address, assetID, 0 - amount, reason);

            return true;
        }

        /***********
         * Deposit *
         ***********/
        private static bool Deposit(byte[] originator, byte[] assetID, byte[] txid)
        {
            if (!Runtime.CheckWitness(originator)) return false;

            // Check that the contract is safe
            if (!GetIsWhitelisted(assetID)) return false;

            TransferInfo tx = GetNep5TxInfo(assetID, txid);

            if (tx.from != originator || tx.to != ExecutionEngine.ExecutingScriptHash || tx.value <= 0) return false;
            // Check address and amounts
            if (originator.Length != 20) return false;

            // Update balances
            if (!IncreaseBalance(originator, assetID, tx.value, ReasonDeposit)) throw new Exception("Failed to increase balance");

            SetNep5TxidUsed(txid);

            EmitDeposited(originator, assetID, tx.value);
            return true;
        }

        private static void SetNep5TxidUsed(byte[] txid)
        {
            var key = TxidUsedKey(txid);
            Storage.Put(Context(), key, 1);
        }

        private static TransferInfo GetNep5TxInfo(byte[] assetID, byte[] txid)
        {
            var tInfo = new TransferInfo();
            var v = Storage.Get(Context(), TxidUsedKey(txid)).AsBigInteger();
            if (v == 0)
            {
                object[] args = new object[1] { txid };
                var contract = (NEP5Contract)assetID.ToDelegate();
                var info = contract("getTxInfo", args);
                if (((object[])info).Length == 3)
                    tInfo = info as TransferInfo;
            }
            return tInfo;
        }

        /***********
         * Withdrawal *
         ***********/
        private static bool Withdrawal(byte[] originator, byte[] withdrawaAssetId, BigInteger withdrawaAmount, byte[] feeAssetId, BigInteger feeAmount)
        {
            if (!Runtime.CheckWitness(originator)) return false;

            if (originator.Length != 20) return false;

            var originatorBalance = GetBalance(originator, withdrawaAssetId);

            if (originatorBalance < withdrawaAmount)
            {
                EmitWithdrawnFailed(originator, withdrawaAssetId, withdrawaAmount, feeAssetId, feeAmount, ReasonNotEnoughBalanceToken);
                return false;
            }

            byte[] feeAddress = Storage.Get(Context(), "feeAddress");

            // 如果 takerFeeAssetID != offer.OfferAssetID，交易费需要单独扣
            bool deductFeesSeparately = feeAssetId != withdrawaAssetId;

            if (deductFeesSeparately)
            {
                var feeBalance = GetBalance(originator, feeAssetId);
                if (feeBalance < feeAmount)
                {
                    EmitWithdrawnFailed(originator, withdrawaAssetId, withdrawaAmount, feeAssetId, feeAmount, ReasonNotEnoughFee);
                    return false;
                }
            }
            else
            {
                withdrawaAmount -= feeAmount;
            }

            //reduce withdrawaAmount
            ReduceBalance(originator, withdrawaAssetId, withdrawaAmount, ReasonWithdraw);

            //redice fee
            ReduceBalance(originator, feeAssetId, feeAmount, ReasonWithdrawFee);

            //add fee
            IncreaseBalance(feeAddress, feeAssetId, feeAmount, ReasonWithdrawFeeReceive);

            //transfer
            if (TransferAppNEP5(ExecutionEngine.ExecutingScriptHash, originator, withdrawaAssetId, withdrawaAmount))
            {
                EmitWithdrawn(originator, withdrawaAssetId, withdrawaAmount);// (address, assetID, amount)
                return true;
            }

            return false;
        }

        private static bool TransferAppNEP5(byte[] from, byte[] to, byte[] assetID, BigInteger amount)
        {
            // Transfer token
            var args = new object[] { from, to, amount };
            var contract = (NEP5Contract)assetID.ToDelegate();
            if ((bool)contract("transfer_app", args)) return true;
            return false;
        }

        private static StorageContext Context() => Storage.CurrentContext;
        private static BigInteger AmountToOffer(Offer o, BigInteger amount) => (o.OfferAmount * amount) / o.WantAmount;

        // Keys
        private static byte[] OfferKey(byte[] offerHash) => "offers".AsByteArray().Concat(offerHash);
        private static byte[] BalanceKey(byte[] originator, byte[] assetID) => "balance".AsByteArray().Concat(originator).Concat(assetID);
        private static byte[] WhitelistKey(byte[] assetId) => "whiteList".AsByteArray().Concat(assetId);
        private static byte[] TxidUsedKey(byte[] txid) => "txidUsed".AsByteArray().Concat(txid);
    }
}